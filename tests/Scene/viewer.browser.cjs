const {chromium}=require('playwright');
const {mkdtempSync,mkdirSync,writeFileSync,readFileSync,rmSync}=require('node:fs');
const {tmpdir}=require('node:os');
const {resolve,join}=require('node:path');
const {spawn}=require('node:child_process');
const assert=require('node:assert/strict');
const root=resolve(__dirname,'../..');
const fixture=mkdtempSync(join(tmpdir(),'topas-viewer-'));
const template=readFileSync(join(root,'tests/Server.Tests/Fixtures/generated-input-1113.txt'),'utf8');
const source=`
s:Ge/GantryFrame/Type = "Group"
s:Ge/GantryFrame/Parent = "World"
s:Ge/PhspFrame/Type = "Group"
s:Ge/PhspFrame/Parent = "GantryFrame"
d:Ge/PhspFrame/TransZ = -733 mm
s:So/Beam/Type = "PhaseSpace"
s:So/Beam/Component = "PhspFrame"
s:So/Beam/PhaseSpaceFileName = "source"
b:So/Beam/PhaseSpaceIgnoreZPos = "true"
s:Ge/TreatmentHead/Type = "TsBox"
s:Ge/TreatmentHead/Parent = "GantryFrame"
d:Ge/TreatmentHead/TransZ = -600 mm
d:Ge/TreatmentHead/TransX = 130 mm
d:Ge/TreatmentHead/HLX = 40 mm
d:Ge/TreatmentHead/HLY = 50 mm
d:Ge/TreatmentHead/HLZ = 30 mm
`;
for(const batch of ['1116','1117']){
    const inputs=join(fixture,'inputs',batch),run=join(fixture,'runs',batch);
    mkdirSync(inputs,{recursive:true});mkdirSync(run,{recursive:true});
    writeFileSync(join(run,'source.header'),'$RECORD_CONTENTS:\n1 // X is stored ?\n1 // Y is stored ?\n0 // Z is stored ?\n$STATISTICAL_INFORMATION_GEOMETRY:\ndirection min max\nx -6.5 6.5\ny -6.5 6.5\n');
    for(let i=0;i<22;i++)writeFileSync(join(inputs,`seed${batch}7_phsp${i.toString().padStart(2,'0')}.txt`),template+source);
}
writeFileSync(join(fixture,'inputs','1117','000-invalid.txt'),'not a geometry');
const backend=spawn(process.env.DOTNET_HOST_PATH||'dotnet',[`${root}/src/Server/bin/Debug/net8.0/Server.dll`],{cwd:`${root}/src/Server`,env:{...process.env,Tsebt__AppRoot:fixture,Tsebt__Paths__Database:'database/app.db',ASPNETCORE_URLS:'http://127.0.0.1:5000'},stdio:['ignore','pipe','pipe']});
const vite=spawn(process.execPath,['node_modules/vite/bin/vite.js','--host','127.0.0.1'],{cwd:`${root}/src/Client`,stdio:['ignore','pipe','pipe']});
let logs='';for(const child of [backend,vite]){child.stdout.on('data',x=>logs+=x);child.stderr.on('data',x=>logs+=x);}
/// Waits for a local server without hiding startup failures.
async function ready(url){for(let i=0;i<150;i++){try{const r=await fetch(url);if(r.status<500)return;}catch{}await new Promise(r=>setTimeout(r,200));}throw Error(logs);}
/// Reads the live camera through the renderer's actual store.
async function camera(page){return page.evaluate(async()=>{const fiber=await import('/node_modules/.vite/deps/@react-three_fiber.js');const s=fiber._roots.get(document.querySelector('.scene-canvas canvas')).store.getState();return {target:s.controls.target.toArray(),distance:s.camera.position.distanceTo(s.controls.target)};});}
/// Projects the selected physical mesh centre into browser coordinates.
async function point(page,name){return page.evaluate(async name=>{const fiber=await import('/node_modules/.vite/deps/@react-three_fiber.js');const THREE=await import('/node_modules/.vite/deps/three.js');const canvas=document.querySelector('.scene-canvas canvas'),s=fiber._roots.get(canvas).store.getState();let mesh;s.scene.traverse(o=>{if(o.isMesh&&o.userData.component===name)mesh=o;});if(!mesh)throw Error(`No mesh ${name}`);mesh.geometry.computeBoundingBox();const p=mesh.geometry.boundingBox.getCenter(new THREE.Vector3());mesh.localToWorld(p);p.project(s.camera);const r=canvas.getBoundingClientRect();return {x:r.x+(p.x+1)*r.width/2,y:r.y+(1-p.y)*r.height/2};},name);}
(async()=>{let browser;try{
await Promise.all([ready('http://127.0.0.1:8080'),ready('http://127.0.0.1:5000')]);
browser=await chromium.launch({headless:true,args:['--no-sandbox','--use-gl=angle','--use-angle=swiftshader','--enable-unsafe-swiftshader']});
const page=await browser.newPage({viewport:{width:1440,height:1000}});const errors=[];page.on('pageerror',e=>errors.push(String(e)));
await page.goto('http://127.0.0.1:8080');await page.getByRole('button',{name:'Geometry',exact:true}).click();
const select=page.getByLabel('Geometry run ID');await page.waitForFunction(()=>document.querySelector('select[aria-label="Geometry run ID"]')?.options.length===3);
assert.deepEqual(await select.locator('option').allTextContents(),['Select a run','1117','1116']);assert.match(await select.inputValue(),/^1117\//);
await page.locator('.scene-canvas canvas').waitFor();await page.waitForTimeout(1500);
assert.equal(await page.locator('.scene-caption').count(),0);assert.equal(await page.getByText('Source squares show local frames',{exact:false}).count(),0);
const all=await camera(page);
await page.locator('.scene-tree-row button').getByText('WaterTank',{exact:true}).dblclick();await page.waitForTimeout(500);
const tank=await camera(page);assert.ok(tank.distance<all.distance*.8,JSON.stringify({all,tank}));
await page.locator('.scene-tree-row button').getByText('PhspFrame',{exact:true}).dblclick();await page.waitForTimeout(500);
const phsp=await camera(page);assert.ok(Math.abs(phsp.target[2]-733)<.1);assert.ok(phsp.distance>200&&phsp.distance<500);assert.ok((await page.locator('.scene-details').innerText()).includes('130.00 × 130.00 × 0.00'));
for(const name of ['TreatmentHead','PhspFrame','TreatmentHead']){
  await page.getByRole('button',{name:'Fit all',exact:true}).click();await page.waitForTimeout(400);
  await page.locator('.scene-tree-row button').getByText('WaterTank',{exact:true}).click();
  const p=await point(page,name);await page.mouse.dblclick(p.x,p.y,{delay:100});await page.waitForTimeout(500);
  const focused=await camera(page);assert.ok(focused.distance<all.distance*.5,`${name} did not focus: ${JSON.stringify(focused)}`);
  assert.equal(await page.locator('.scene-tree-row.selected button').innerText(),name);
  if(name==='PhspFrame')assert.ok(Math.abs(focused.target[2]-733)<.1);
}
await page.getByLabel('Sources',{exact:true}).uncheck();await page.waitForTimeout(300);const before=await camera(page);await page.locator('.scene-tree-row button').getByText('WaterTank',{exact:true}).click();await page.waitForTimeout(300);assert.deepEqual(await camera(page),before);
await select.selectOption({label:'1116'});await page.waitForTimeout(700);await page.getByRole('button',{name:'Refresh',exact:true}).click();await page.waitForTimeout(700);assert.match(await select.inputValue(),/^1116\//);
await page.setViewportSize({width:390,height:844});await page.waitForTimeout(300);assert.ok(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth));assert.deepEqual(errors,[]);console.log('PASS: run grouping, auto-selection, refresh preservation, tree/mesh/PHSP repeated double-click, exact PHSP size, stable transparent picking, no footer, responsive layout');
}catch(e){console.error(e);console.error(logs.slice(-4000));process.exitCode=1;}finally{if(browser)await browser.close();backend.kill();vite.kill();rmSync(fixture,{recursive:true,force:true});}})();
