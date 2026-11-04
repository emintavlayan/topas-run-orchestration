import React,{useEffect,useRef,useState} from 'react';
import renderer from '@jscad/regl-renderer';
import {buildSolids,bounds,worldMatrix,transformPoint,included,scorerSegments,displayPoint,displaySolid} from './SceneGeometry.mjs';
const {cameras,controls,drawCommands,entitiesFromSolids,prepareRender}=renderer;

/// Builds a line entity by mapping physical world coordinates into the shared display frame.
function lines(segments,color){const positions=segments.flat().map(displayPoint);return {geometry:{positions,normals:positions.map(()=>[0,0,1]),indices:positions.map((_,i)=>i),color},visuals:{drawCmd:'drawLines',show:true,transparent:color[3]<1}};}
/// Adds local component axes and source direction guides without inventing field apertures.
function guides(components,sources,hidden,selected,span,showAxes){
    const entities=[],length=Math.max(5,span*.08);
    for(const c of components.filter(c=>included(c.name,components,hidden)&&(c.name==='World'||c.name===selected||sources.some(s=>s.component===c.name)))){
        const m=worldMatrix(c.name,components),p=transformPoint(m,[0,0,0]);
        if(showAxes)for(let axis=0;axis<3;axis++){const end=[0,0,0];end[axis]=length;entities.push(lines([[p,transformPoint(m,end)]],[[.9,.2,.2,1],[.2,.7,.3,1],[.2,.45,1,1]][axis]));}
        if(sources.some(s=>s.component===c.name)){
            const end=transformPoint(m,[0,0,length*3]);entities.push(lines([[p,end]], [.8,.2,.8,1]));
            entities.push(lines([[[ -length/2,-length/2,0],[length/2,-length/2,0]],[[length/2,-length/2,0],[length/2,length/2,0]],[[length/2,length/2,0],[-length/2,length/2,0]],[[-length/2,length/2,0],[-length/2,-length/2,0]]].map(segment=>segment.map(v=>transformPoint(m,v))),[.8,.2,.8,1]));
        }
    }
    return entities;
}
/// Owns the WebGL canvas, orbit controls, resize handling and disposal.
export default function SceneCanvas({components,scorers,sources,assets,hidden,selected,reset,view,showAxes,showSources}){
    const host=useRef(null),saved=useRef(null),lastReset=useRef(null);const [error,setError]=useState('');
    useEffect(()=>{
        const container=host.current;let dispose=()=>{};
        try{
            const built=buildSolids(components,scorers,assets,hidden,selected);setError(built.warnings.join('\n'));
            const physicalExtent=bounds(built.solids,components,hidden),extent={...physicalExtent,center:displayPoint(physicalExtent.center)},perspective=cameras.perspective,orbit=controls.orbit;
            const camera={...perspective.defaults,near:Math.max(.01,extent.span/10000),far:Math.max(18000,extent.span*20),position:extent.center.map((v,i)=>v+[1.4,-1.8,1.2][i]*extent.span),target:[...extent.center]};
            if(saved.current&&lastReset.current===reset)Object.assign(camera,saved.current);
            else if(view!=='orbit'){
                const direction={front:[0,0,-2],side:[2,0,0],top:[0,2,0]}[view];camera.position=extent.center.map((v,i)=>v+displayPoint(direction)[i]*extent.span);if(view==='front')camera.up=[0,1,0];
            }
            lastReset.current=reset;
            let control={...orbit.defaults},frame,dirty=true,drag=null;
            const canvas=document.createElement('canvas');canvas.style.cssText='width:100%;height:100%;display:block;touch-action:none';container.appendChild(canvas);
            const render=prepareRender({glOptions:{canvas}});
            const options={camera,drawCommands:{drawMesh:drawCommands.drawMesh,drawLines:drawCommands.drawLines,drawAxis:drawCommands.drawAxis},entities:[...entitiesFromSolids({smoothNormals:false},...built.solids.map(displaySolid)),...guides(components,showSources?sources:[],hidden,selected,extent.span,showAxes),...components.filter(c=>included(c.name,components,hidden)).map(c=>lines(scorerSegments(c,scorers).map(segment=>segment.map(p=>transformPoint(worldMatrix(c.name,components),p))),[.7,.08,.2,1])).filter(e=>e.geometry.positions.length)],rendering:{background:[.95,.97,.98,1]}};
            /// Resizes the drawing buffer to match the available panel.
            function resize(){const width=Math.max(1,container.clientWidth),height=Math.max(1,container.clientHeight);canvas.width=Math.round(width*Math.min(2,window.devicePixelRatio||1));canvas.height=Math.round(height*Math.min(2,window.devicePixelRatio||1));perspective.setProjection(camera,camera,{width,height});perspective.update(camera,camera);dirty=true;}
            /// Applies pointer movement through the renderer's orbit controller.
            function move(event){if(!drag)return;const delta=[event.clientX-drag[0],drag[1]-event.clientY];drag=[event.clientX,event.clientY];const updated=event.shiftKey?orbit.pan({controls:control,camera},[-delta[0],delta[1]]):orbit.rotate({controls:control,camera,speed:.006},delta);control={...control,...updated.controls};if(updated.camera)Object.assign(camera,updated.camera);dirty=true;}
            /// Captures the active pointer for continuous orbit or pan gestures.
            function down(event){drag=[event.clientX,event.clientY];canvas.setPointerCapture(event.pointerId);}
            /// Releases drag state when a pointer is released or cancelled.
            function up(event){drag=null;if(canvas.hasPointerCapture(event.pointerId))canvas.releasePointerCapture(event.pointerId);}
            /// Zooms the scene while preserving the orbit target.
            function wheel(event){event.preventDefault();const updated=orbit.zoom({controls:control,camera,speed:.08},event.deltaY);control={...control,...updated.controls};dirty=true;}
            /// Redraws only when the scene or orbit controller has changed.
            function tick(){if(dirty){const updated=orbit.update({controls:control,camera});control={...control,...updated.controls};if(updated.camera)Object.assign(camera,updated.camera);perspective.update(camera,camera);render(options);dirty=!!control.changed;}frame=requestAnimationFrame(tick);}
            canvas.addEventListener('pointerdown',down);canvas.addEventListener('pointermove',move);canvas.addEventListener('pointerup',up);canvas.addEventListener('pointercancel',up);canvas.addEventListener('wheel',wheel,{passive:false});
            const observer=new ResizeObserver(resize);observer.observe(container);resize();tick();
            dispose=()=>{saved.current={position:[...camera.position],target:[...camera.target],up:[...camera.up]};cancelAnimationFrame(frame);observer.disconnect();canvas.removeEventListener('pointerdown',down);canvas.removeEventListener('pointermove',move);canvas.removeEventListener('pointerup',up);canvas.removeEventListener('pointercancel',up);canvas.removeEventListener('wheel',wheel);canvas.getContext('webgl')?.getExtension('WEBGL_lose_context')?.loseContext();canvas.remove();};
        }catch(error){container.replaceChildren();setError(`3D view unavailable: ${error.message}`);}
        return dispose;
    },[components,scorers,sources,assets,hidden,selected,reset,view,showAxes,showSources]);
    return <div className="scene-canvas-shell"><div ref={host} className="scene-canvas" aria-label="Simulation geometry 3D view"/>{error&&<pre role="alert" className="scene-render-error">{error}</pre>}</div>;
}
