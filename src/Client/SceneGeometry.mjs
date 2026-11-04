import modeling from '@jscad/modeling';
import stl from '@jscad/stl-deserializer';
const {primitives, transforms, booleans, colors, geometries} = modeling;

/// Maps a physical TOPAS point into BeamHead's display convention with positive Z downward.
export function displayPoint(point) { return [point[0],point[1],-point[2]]; }
/// Mirrors a completed world solid only for display, preserving JSCAD polygon winding.
export function displaySolid(solid) { return transforms.mirrorZ(solid); }

/// Multiplies column-major matrices without changing either operand.
export function multiply(a,b) {
    return Array.from({length:16},(_,i)=>Array.from({length:4},(_,k)=>a[k*4+i%4]*b[Math.floor(i/4)*4+k]).reduce((s,x)=>s+x,0));
}
/// Creates the identity transform in the original simulation coordinate system.
export function identity() { return [1,0,0,0,0,1,0,0,0,0,1,0,0,0,0,1]; }
/// Constructs TOPAS intrinsic XYZ placement as translation times Rx times Ry times Rz.
export function placement(translation,rotation) {
    const [x,y,z]=rotation.map(d=>d*Math.PI/180), cx=Math.cos(x),sx=Math.sin(x),cy=Math.cos(y),sy=Math.sin(y),cz=Math.cos(z),sz=Math.sin(z);
    const rx=[1,0,0,0,0,cx,sx,0,0,-sx,cx,0,0,0,0,1];
    const ry=[cy,0,-sy,0,0,1,0,0,sy,0,cy,0,0,0,0,1];
    const rz=[cz,sz,0,0,-sz,cz,0,0,0,0,1,0,0,0,0,1];
    const m=multiply(multiply(rx,ry),rz); m[12]=translation[0];m[13]=translation[1];m[14]=translation[2]; return m;
}
/// Resolves a complete parent chain and preserves local coordinates for descendants.
export function worldMatrix(name,components,seen=new Set()) {
    if(seen.has(name)) throw Error(`Cyclic component: ${name}`);
    const c=components.find(c=>c.name===name); if(!c) throw Error(`Missing component: ${name}`);
    const local=placement(c.translation,c.rotation);
    return c.parent?multiply(worldMatrix(c.parent,components,new Set([...seen,name])),local):local;
}
/// Transforms a local point into its parent or world frame.
export function transformPoint(m,p) {return [0,1,2].map(i=>m[i]*p[0]+m[4+i]*p[1]+m[8+i]*p[2]+m[12+i]);}
/// Checks whether a component and all its ancestors are enabled.
export function included(name,components,hidden=[]) {
    const c=components.find(c=>c.name===name);return !!c && c.included && !hidden.includes(name) && (!c.parent||included(c.parent,components,hidden));
}
/// Converts a supported solid to JSCAD using physical millimetres.
export function localSolid(c,assets) {
    if(c.kind==='box') return primitives.cuboid({size:Array.from(c.dimensions,x=>2*x)});
    if(c.kind==='cylinder') {
        const [inner,outer,half,start,sweep]=c.dimensions;
        const make=r=>sweep===360?primitives.cylinder({radius:r,height:2*half,segments:64}):primitives.cylinderElliptic({height:2*half,startRadius:[r,r],endRadius:[r,r],startAngle:start*Math.PI/180,endAngle:(start+sweep)*Math.PI/180,segments:64});
        return inner>0?booleans.subtract(make(outer),make(inner)):make(outer);
    }
    if(c.kind==='cad' && assets[c.name]) {
        const bytes=Uint8Array.from(atob(assets[c.name]),x=>x.charCodeAt(0));
        const solids=stl.deserialize({output:'geometry'},bytes.buffer);
        return transforms.scale([c.unit,c.unit,c.unit],...solids);
    }
    return null;
}
/// Resolves a display colour without assigning physical material properties.
export function componentColor(c,selected,scored) {
    if(selected)return [1,.65,.08,.85];
    if(scored)return [.91,.22,.32,.8];
    const palette={blue:[.2,.5,.9,.18],red:[.9,.2,.25,.7],green:[.2,.7,.4,.5],gray:[.5,.55,.6,.65],grey:[.5,.55,.6,.65],yellow:[.95,.8,.15,.65],white:[.8,.85,.9,.55]};
    return palette[c.color?.toLowerCase()]??(c.material?.includes('WATER')?[.2,.6,.9,.2]:[.4,.55,.7,.65]);
}
/// Builds world-positioned solids and retains labels for actionable render failures.
export function buildSolids(components,scorers,assets,hidden,selected) {
    const solids=[],warnings=[];
    for(const c of components) {
        if(c.name==='World'||!included(c.name,components,hidden))continue;
        try {
            let solid=localSolid(c,assets);
            if(solid){if(!Array.isArray(solid))solid=[solid];for(const s of solid)solids.push(colors.colorize(componentColor(c,c.name===selected,scorers.some(s=>s.component===c.name&&s.active)),transforms.transform(worldMatrix(c.name,components),s)));}
        } catch(error){warnings.push(`${c.name}: ${error.message}`);}
    }
    return {solids,warnings};
}
/// Reads resolved polygon vertices for frame and mesh validation.
export function vertices(solid){return geometries.geom3.toPolygons(solid).flatMap(p=>p.vertices);}
/// Computes a finite scene centre and span without including the World enclosure.
export function bounds(solids,components,hidden=[]) {
    const points=[...solids.flatMap(vertices),...components.filter(c=>c.name!=='World'&&included(c.name,components,hidden)).map(c=>transformPoint(worldMatrix(c.name,components),[0,0,0]))];
    if(!points.length)return {center:[0,0,0],span:1000};
    const min=[Infinity,Infinity,Infinity],max=[-Infinity,-Infinity,-Infinity];
    for(const point of points)for(let i=0;i<3;i++){min[i]=Math.min(min[i],point[i]);max[i]=Math.max(max[i],point[i]);}
    return {center:min.map((v,i)=>(v+max[i])/2),span:Math.max(10,...min.map((v,i)=>max[i]-v))};
}

/// Builds box scoring surfaces and bounded one-dimensional bin dividers in local coordinates.
export function scorerSegments(c,scorers) {
    const segments=[];
    for(const scorer of scorers.filter(s=>s.active&&s.component===c.name)){
        if(c.kind==='box'){
            const h=c.dimensions,surface=/^([XYZ])(Minus|Plus)Surface$/.exec(scorer.surface??'');
            if(surface){const axis='XYZ'.indexOf(surface[1]),other=[0,1,2].filter(i=>i!==axis),points=[[-1,-1],[1,-1],[1,1],[-1,1]].map(signs=>{const p=[0,0,0];p[axis]=h[axis]*(surface[2]==='Plus'?1:-1);other.forEach((i,j)=>p[i]=signs[j]*h[i]);return p;});points.forEach((p,i)=>segments.push([p,points[(i+1)%4]]));}
            for(let axis=0;axis<3;axis++){
                const count=scorer.bins?.['XYZ'[axis]]??1;
                if(count<=1||count>512)continue;
                const other=[0,1,2].filter(i=>i!==axis);
                for(let j=1;j<count;j++){const p=[...h],q=[...h];p[axis]=q[axis]=-h[axis]+2*h[axis]*j/count;p[other[0]]=-h[other[0]];q[other[0]]=h[other[0]];segments.push([p,q]);}
            }
        }else if(c.kind==='cylinder'){
            const [inner,radius,half,start,sweep]=c.dimensions,count=scorer.bins?.Z??1;
            const levels=scorer.surface==='ZMinusSurface'?[-half]:scorer.surface==='ZPlusSurface'?[half]:count>1&&count<=512?Array.from({length:count-1},(_,j)=>-half+2*half*(j+1)/count):[];
            for(const z of levels)for(let i=0;i<64;i++){const a=(start+sweep*i/64)*Math.PI/180,b=(start+sweep*(i+1)/64)*Math.PI/180;segments.push([[radius*Math.cos(a),radius*Math.sin(a),z],[radius*Math.cos(b),radius*Math.sin(b),z]]);}
        }
    }
    return segments;
}
