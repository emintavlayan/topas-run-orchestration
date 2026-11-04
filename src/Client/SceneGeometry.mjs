import * as THREE from 'three';
import {STLLoader} from 'three/addons/loaders/STLLoader.js';

/// Maps physical TOPAS coordinates into the established Z-down display frame.
export function displayPoint(point) { return [point[0],point[1],-point[2]]; }
/// Creates an identity matrix in physical coordinates.
export function identity() { return new THREE.Matrix4().toArray(); }
/// Multiplies column-major frame transforms without mutating either input.
export function multiply(a,b) { return new THREE.Matrix4().fromArray(a).multiply(new THREE.Matrix4().fromArray(b)).toArray(); }
/// Constructs intrinsic XYZ rotation followed by translation in the parent frame.
export function placement(translation,rotation) {
    return new THREE.Matrix4().makeRotationFromEuler(new THREE.Euler(...Array.from(rotation,x=>THREE.MathUtils.degToRad(x)),'XYZ')).setPosition(...translation).toArray();
}
/// Resolves a component's explicit parent chain into a physical world transform.
export function worldMatrix(name,components,seen=new Set()) {
    if(seen.has(name))throw Error(`Cyclic component: ${name}`);
    const component=components.find(c=>c.name===name);if(!component)throw Error(`Missing component: ${name}`);
    const local=placement(component.translation,component.rotation);
    return component.parent?multiply(worldMatrix(component.parent,components,new Set([...seen,name])),local):local;
}
/// Transforms a local point into a physical world frame.
export function transformPoint(matrix,point) { return new THREE.Vector3(...point).applyMatrix4(new THREE.Matrix4().fromArray(matrix)).toArray(); }
/// Checks whether a component belongs to the selected subtree.
export function within(name,root,components) {
    if(!root||name===root)return true;
    const component=components.find(c=>c.name===name);
    return !!component?.parent&&within(component.parent,root,components);
}
/// Checks explicit inclusion and ancestor visibility.
export function included(name,components,hidden=[]) {
    const component=components.find(c=>c.name===name);
    return !!component&&component.included&&!hidden.includes(name)&&(!component.parent||included(component.parent,components,hidden));
}
/// Creates a closed cylindrical solid or annular sector aligned with local Z.
export function cylinderGeometry(dimensions) {
    const [inner,outer,half,start,sweep]=dimensions,a=THREE.MathUtils.degToRad(start),b=THREE.MathUtils.degToRad(start+sweep);
    const shape=new THREE.Shape();shape.absarc(0,0,outer,a,b,false);
    if(sweep>=360){
        if(inner>0){const hole=new THREE.Path();hole.absarc(0,0,inner,a,b,true);shape.holes.push(hole);}
    }else if(inner>0){shape.lineTo(inner*Math.cos(b),inner*Math.sin(b));shape.absarc(0,0,inner,b,a,true);shape.closePath();}
    else {shape.lineTo(0,0);shape.closePath();}
    const geometry=new THREE.ExtrudeGeometry(shape,{depth:2*half,bevelEnabled:false,curveSegments:32,steps:1});geometry.translate(0,0,-half);return geometry;
}
/// Creates viewer meshes directly from primitives or STL bytes in physical millimetres.
export function localGeometry(component,assets) {
    if(component.kind==='box')return new THREE.BoxGeometry(...Array.from(component.dimensions,x=>2*x));
    if(component.kind==='cylinder')return cylinderGeometry(component.dimensions);
    if(component.kind==='cad'&&assets[component.name]){
        const bytes=Uint8Array.from(atob(assets[component.name]),x=>x.charCodeAt(0));
        const geometry=new STLLoader().parse(bytes.buffer);geometry.scale(component.unit,component.unit,component.unit);geometry.computeVertexNormals();return geometry;
    }
    return null;
}
/// Builds reusable physical geometries and reports individual mesh errors.
export function buildGeometries(components,assets) {
    const entries=[],warnings=[];
    for(const component of components){
        if(component.name==='World')continue;
        try{const geometry=localGeometry(component,assets);if(geometry)entries.push({name:component.name,geometry,matrix:new THREE.Matrix4().fromArray(worldMatrix(component.name,components))});}
        catch(error){warnings.push(`${component.name}: ${error.message}`);}
    }
    return {entries,warnings};
}
/// Reads physical vertices for independent frame and dimension checks.
export function vertices(geometry,matrix=new THREE.Matrix4()) {
    const attribute=geometry.getAttribute('position');return Array.from({length:attribute.count},(_,index)=>new THREE.Vector3().fromBufferAttribute(attribute,index).applyMatrix4(matrix).toArray());
}
/// Computes display bounds for visible meshes and an optional focused subtree.
export function sceneBounds(entries,components,hidden=[],focused='',isolated='') {
    const box=new THREE.Box3(),reflection=new THREE.Matrix4().makeScale(1,1,-1);
    for(const entry of entries){
        if(!included(entry.name,components,hidden)||!within(entry.name,focused,components)||!within(entry.name,isolated,components))continue;
        entry.geometry.computeBoundingBox();box.union(entry.geometry.boundingBox.clone().applyMatrix4(entry.matrix).applyMatrix4(reflection));
    }
    for(const component of components){
        if(component.name!=='World'&&included(component.name,components,hidden)&&within(component.name,focused,components)&&within(component.name,isolated,components)&&(component.kind==='group'||component.kind==='unsupported'))box.expandByPoint(new THREE.Vector3(...displayPoint(transformPoint(worldMatrix(component.name,components),[0,0,0]))));
    }
    if(box.isEmpty()){
        const component=components.find(c=>c.name===focused);
        const center=component?displayPoint(transformPoint(worldMatrix(component.name,components),[0,0,0])):[0,0,0];
        box.setFromCenterAndSize(new THREE.Vector3(...center),new THREE.Vector3(100,100,100));
    }
    return box;
}
/// Calculates an aspect-aware camera distance that contains the entire bounding sphere.
export function fitDistance(box,verticalFov,aspect) {
    const radius=Math.max(1,box.getSize(new THREE.Vector3()).length()/2);
    const halfAngle=Math.min(THREE.MathUtils.degToRad(verticalFov)/2,Math.atan(Math.tan(THREE.MathUtils.degToRad(verticalFov)/2)*aspect));
    return radius/Math.sin(halfAngle)*1.12;
}
/// Builds scoring boundaries using actual local bin positions with bounded detail.
export function scorerSegments(component,scorers) {
    const segments=[];
    for(const scorer of scorers.filter(s=>s.active&&s.component===component.name)){
        if(component.kind==='box'){
            const half=component.dimensions,surface=/^([XYZ])(Minus|Plus)Surface$/.exec(scorer.surface??'');
            if(surface){const axis='XYZ'.indexOf(surface[1]),other=[0,1,2].filter(i=>i!==axis),points=[[-1,-1],[1,-1],[1,1],[-1,1]].map(signs=>{const point=[0,0,0];point[axis]=half[axis]*(surface[2]==='Plus'?1:-1);other.forEach((i,j)=>point[i]=signs[j]*half[i]);return point;});points.forEach((point,i)=>segments.push([point,points[(i+1)%4]]));}
            for(let axis=0;axis<3;axis++){
                const count=scorer.bins?.['XYZ'[axis]]??1;if(count<=1||count>512)continue;
                const other=[0,1,2].filter(i=>i!==axis);
                for(let j=1;j<count;j++){const p=[...half],q=[...half];p[axis]=q[axis]=-half[axis]+2*half[axis]*j/count;p[other[0]]=-half[other[0]];segments.push([p,q]);}
            }
        }else if(component.kind==='cylinder'){
            const [,radius,half,start,sweep]=component.dimensions,count=scorer.bins?.Z??1;
            const levels=scorer.surface==='ZMinusSurface'?[-half]:scorer.surface==='ZPlusSurface'?[half]:count>1&&count<=512?Array.from({length:count-1},(_,j)=>-half+2*half*(j+1)/count):[];
            for(const z of levels)for(let i=0;i<64;i++){const a=THREE.MathUtils.degToRad(start+sweep*i/64),b=THREE.MathUtils.degToRad(start+sweep*(i+1)/64);segments.push([[radius*Math.cos(a),radius*Math.sin(a),z],[radius*Math.cos(b),radius*Math.sin(b),z]]);}
        }
    }
    return segments;
}
