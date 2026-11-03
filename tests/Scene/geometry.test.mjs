import assert from 'node:assert/strict';
import {test} from 'node:test';
import {placement,transformPoint,worldMatrix,included,localSolid,vertices,buildSolids,bounds} from '../../src/Client/SceneGeometry.mjs';
/// Compares transformed coordinates against independently calculated values.
function near(actual,expected){actual.forEach((v,i)=>assert.ok(Math.abs(v-expected[i])<1e-8,`${actual} != ${expected}`));}
/// Creates a renderer-boundary component with an explicit local frame.
function component(name,parent=null,translation=[0,0,0],rotation=[0,0,0]){return {name,parent,translation,rotation,included:true,kind:'box',dimensions:[1,2,3]};}
test('nested gantry rotation moves child translation in parent axes',()=>{
 const components=[component('World'),component('Gantry','World',[10,20,30],[0,90,0]),component('Phantom','Gantry',[0,0,100])];
 near(transformPoint(worldMatrix('Phantom',components),[0,0,0]),[110,20,30]);
 near(transformPoint(worldMatrix('Phantom',components),[1,0,0]),[110,20,29]);
});
test('intrinsic XYZ rotation order is preserved',()=>near(transformPoint(placement([0,0,0],[90,90,0]),[0,0,1]),[1,0,0]));
test('units are used without camera or display mirroring',()=>{
 const c=component('Box');c.translation=[10,20,30];const {solids}=buildSolids([c],[],{},[],'');
 const result=bounds(solids,[c]);near(result.center,[10,20,30]);assert.equal(result.span,10);
 const points=vertices(solids[0]);assert.equal(Math.min(...points.map(p=>p[2])),27);
});
test('excluded ancestors disable descendants',()=>{const components=[component('World'),component('A','World')];assert.equal(included('A',components,['World']),false);components[0].included=false;assert.equal(included('A',components),false);});
test('hollow and sector cylinders have physical extents',()=>{
 const solid=localSolid({...component('Cylinder'),kind:'cylinder',dimensions:[2,5,10,0,180]},{});const points=vertices(solid);assert.ok(points.length>0);assert.ok(points.every(p=>p[1]>=-1e-8));assert.equal(Math.max(...points.map(p=>p[2])),10);
});
test('ASCII STL is imported and scaled in its original coordinate frame',()=>{
 const text='solid test\nfacet normal 0 0 1\nouter loop\nvertex 1 0 0\nvertex 0 1 0\nvertex 0 0 0\nendloop\nendfacet\nendsolid test';
 const solid=localSolid({...component('CAD'),kind:'cad',unit:10},{CAD:Buffer.from(text).toString('base64')});near(vertices(solid[0]??solid)[0],[10,0,0]);
});
test('scorer dividers retain real bin positions and surface ownership',async()=>{
 const {scorerSegments}=await import('../../src/Client/SceneGeometry.mjs');const c=component('Box');
 const dividers=scorerSegments(c,[{component:'Box',active:true,bins:{X:2}}]);assert.equal(dividers.length,1);assert.equal(dividers[0][0][0],0);
 const face=scorerSegments(c,[{component:'Box',active:true,surface:'ZMinusSurface'}]);assert.equal(face.length,4);assert.ok(face.flat().every(p=>p[2]===-3));
});
test('binary STL uses physical scaling',()=>{
 const bytes=Buffer.alloc(134);bytes.writeUInt32LE(1,80);[[1,0,0],[0,1,0],[0,0,0]].flat().forEach((value,i)=>bytes.writeFloatLE(value,96+i*4));
 const solid=localSolid({...component('CAD'),kind:'cad',unit:2},{CAD:bytes.toString('base64')});assert.ok(vertices(solid[0]??solid).some(p=>p[0]===2&&p[1]===0&&p[2]===0));
});
test('Fable typed numeric arrays are accepted by JSCAD primitives',()=>{
 const solid=localSolid({...component('Box'),dimensions:new Float64Array([1,2,3])},{});assert.ok(vertices(solid).length>0);
});
