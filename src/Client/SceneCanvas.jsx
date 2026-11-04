import React,{Suspense,useEffect,useLayoutEffect,useMemo,useRef,useState} from 'react';
import {Canvas,useThree} from '@react-three/fiber';
import {OrbitControls,GizmoHelper,GizmoViewcube,Edges} from '@react-three/drei';
import * as THREE from 'three';
import {buildGeometries,sceneBounds,fitDistance,worldMatrix,transformPoint,included,within,scorerSegments} from './SceneGeometry.mjs';

/// Contains WebGL failures within the inspector and offers an explicit recovery action.
class ViewErrorBoundary extends React.Component {
    /// Initializes the recoverable renderer error state.
    constructor(props){super(props);this.state={error:null};}
    /// Captures renderer failures without replacing the workspace.
    static getDerivedStateFromError(error){return {error};}
    /// Offers recovery while retaining all scene data.
    render(){return this.state.error?<div role="alert" className="scene-render-error">3D view unavailable: {this.state.error.message}<button onClick={()=>this.setState({error:null})}>Retry 3D view</button></div>:this.props.children;}
}
/// Keeps the existing camera while selecting or hiding objects and refits only on request or resize.
function CameraRig({entries,components,hidden,isolated,focused,reset,view}){
    const {camera,controls,size,invalidate}=useThree();
    const previous=useRef(null);
    useLayoutEffect(()=>{
        if(!controls)return;
        const box=sceneBounds(entries,components,hidden,focused,isolated),center=box.getCenter(new THREE.Vector3());
        const preserve=previous.current===reset||view==='preserve';
        const direction=preserve?camera.position.clone().sub(controls.target).normalize():new THREE.Vector3(...({front:[0,0,1],side:[1,0,0],top:[0,1,0],orbit:[1.4,-1.8,1.2]}[view]??[1.4,-1.8,1.2])).normalize();
        if(!preserve)camera.up.set(...(Math.abs(direction.z)>.99?[0,1,0]:[0,0,1]));
        camera.position.copy(center).addScaledVector(direction,fitDistance(box,camera.fov,size.width/size.height));
        const span=Math.max(10,box.getSize(new THREE.Vector3()).length());camera.near=Math.max(.01,span/10000);camera.far=Math.max(10000,span*100);camera.updateProjectionMatrix();controls.target.copy(center);controls.update();previous.current=reset;invalidate();
    },[controls,entries,reset,size.width,size.height]);
    return null;
}
/// Draws physical line segments under the same display transform as the meshes.
function Segments({segments,color,opacity=1}){
    const key=JSON.stringify(segments);
    const geometry=useMemo(()=>new THREE.BufferGeometry().setFromPoints(segments.flat().map(point=>new THREE.Vector3(...point))),[key]);
    useEffect(()=>()=>geometry.dispose(),[geometry]);
    return segments.length?<lineSegments geometry={geometry} renderOrder={10}><lineBasicMaterial color={color} transparent={opacity<1} opacity={opacity} depthWrite={false}/></lineSegments>:null;
}
/// Builds axes and source frame guides in physical world coordinates.
function FrameGuides({components,sources,hidden,isolated,selected,span,showAxes,showSources}){
    const length=Math.max(5,span*.06);
    return components.filter(c=>included(c.name,components,hidden)&&within(c.name,isolated,components)&&(c.name==='World'||c.name===selected||showSources&&sources.some(s=>s.component===c.name))).map(component=>{
        const matrix=worldMatrix(component.name,components),point=transformPoint(matrix,[0,0,0]);
        const source=showSources&&sources.some(s=>s.component===component.name);
        const square=[[-1,-1],[1,-1],[1,1],[-1,1]].map(([x,y])=>transformPoint(matrix,[x*length/2,y*length/2,0]));
        return <group key={component.name}>{showAxes&&['#dc4141','#23965c','#267ce1'].map((color,axis)=>{const end=[0,0,0];end[axis]=length;return <Segments key={axis} color={color} segments={[[point,transformPoint(matrix,end)]]}/>;})}{source&&<Segments color="#b526d0" segments={[[point,transformPoint(matrix,[0,0,length*3])],...square.map((p,i)=>[p,square[(i+1)%4]])]}/>}</group>;
    });
}
/// Chooses a component behind a transparent enclosure when another visible mesh is hit.
function pickName(event){
    const hits=event.intersections.filter(hit=>hit.object.userData.component);
    return (hits.find(hit=>hit.object.material?.opacity>=.4)??hits[0])?.object.userData.component;
}
/// Displays one selectable physical mesh using inspectable materials and optional outlines.
function ComponentMesh({entry,component,scored,selected,mode,onSelect,onFocus}){
    const [hovered,setHovered]=useState(false),active=entry.name===selected;
    const color=active?'#e7a21a':hovered?'#7ab6eb':scored?'#d94660':component.material?.includes('WATER')?'#3c9ed8':(THREE.Color.NAMES[component.color?.toLowerCase()]!==undefined?component.color:'#8096ad');
    const opacity=mode==='solid'?1:scored ? .85 : component.kind==='cad' ? .65 : .23;
    return <group matrix={entry.matrix} matrixAutoUpdate={false}>
        <mesh geometry={entry.geometry} userData={{component:entry.name}} onClick={event=>{event.stopPropagation();const name=pickName(event);if(name)onSelect?.(name);}} onDoubleClick={event=>{event.stopPropagation();const name=pickName(event);if(name){onSelect?.(name);onFocus(name);}}} onPointerOver={event=>{event.stopPropagation();setHovered(true);}} onPointerOut={()=>setHovered(false)}>
            <meshStandardMaterial color={color} roughness={.7} metalness={0} transparent={opacity<1} opacity={active?Math.max(.5,opacity):opacity} depthWrite={opacity===1} side={THREE.DoubleSide} wireframe={mode==='wireframe'}/>
            {mode!=='wireframe'&&<Edges threshold={25} color={active?'#995c00':scored?'#9f2344':'#47718a'} transparent opacity={active ? .9 : .45}/>}
        </mesh>
    </group>;
}
/// Renders the original simulation geometry beneath a single display-only Z reflection.
function Contents({entries,components,scorers,sources,assets,hidden,isolated,selected,mode,showAxes,showSources,onSelect,onFocus}){
    const span=Math.max(10,sceneBounds(entries,components,hidden,'',isolated).getSize(new THREE.Vector3()).length());
    return <group scale={[1,1,-1]}>{entries.filter(entry=>included(entry.name,components,hidden)&&within(entry.name,isolated,components)).map(entry=><ComponentMesh key={entry.name} entry={entry} component={components.find(c=>c.name===entry.name)} scored={scorers.some(s=>s.component===entry.name&&s.active)} selected={selected} mode={mode} onSelect={onSelect} onFocus={onFocus}/>)}<FrameGuides {...{components,sources,hidden,isolated,selected,span,showAxes,showSources}}/>{components.filter(c=>included(c.name,components,hidden)&&within(c.name,isolated,components)).map(component=><Segments key={component.name} color="#bc2448" segments={scorerSegments(component,scorers).map(segment=>segment.map(point=>transformPoint(worldMatrix(component.name,components),point)))}/>)}</group>;
}
/// Owns a persistent Three.js canvas with standard navigation, selection and orientation controls.
export default function SceneCanvas({components,scorers,sources,assets,hidden,selected,reset,view,showAxes,showSources,onSelect,focused='',isolated='',mode='transparent'}){
    const signature=JSON.stringify([components,assets]);
    const built=useMemo(()=>buildGeometries(components,assets),[signature]);
    const [localFocus,setLocalFocus]=useState({name:'',revision:0});
    useEffect(()=>{setLocalFocus({name:'',revision:0});},[reset]);
    useEffect(()=>()=>built.entries.forEach(entry=>entry.geometry.dispose()),[built]);
    const focusName=localFocus.revision?localFocus.name:focused;
    return <div className="scene-canvas-shell"><ViewErrorBoundary key={signature}>
        <Canvas className="scene-canvas" frameloop="demand" dpr={[1,2]} camera={{fov:40,position:[1400,-1800,1200],up:[0,0,1],near:.1,far:100000}} gl={{antialias:true,alpha:false}} onPointerMissed={()=>onSelect?.('')}>
            <color attach="background" args={['#eff5f9']}/><ambientLight intensity={1.1}/><directionalLight position={[2000,-3000,4000]} intensity={2}/><directionalLight position={[-1000,2000,-1000]} intensity={.7}/>
            <OrbitControls makeDefault enableDamping dampingFactor={.12} minDistance={.1} maxDistance={10000000}/>
            <CameraRig {...{entries:built.entries,components,hidden,isolated,focused:focusName,reset:`${reset}:${localFocus.revision}`,view:localFocus.revision?'preserve':view}}/>
            <Suspense fallback={null}><Contents {...{entries:built.entries,components,scorers,sources,assets,hidden,isolated,selected,mode,showAxes,showSources,onSelect}} onFocus={name=>setLocalFocus(current=>({name,revision:current.revision+1}))}/></Suspense>
            <GizmoHelper alignment="bottom-right" margin={[65,65]}><GizmoViewcube faces={['+X','−X','+Y','−Y','−Z','+Z']} color="#e2eaf0" hoverColor="#b8d6ed" textColor="#173044" strokeColor="#658097" font="40px system-ui"/></GizmoHelper>
        </Canvas>
        <div className="scene-selection-hint">{selected||'Click a component to inspect'}{selected&&<span> · Double-click to focus</span>}</div>
        <div className="scene-scale-hint">TOPAS axes · mm</div>
    </ViewErrorBoundary>{built.warnings.length>0&&<pre role="alert" className="scene-render-error">{built.warnings.join('\n')}</pre>}</div>;
}
