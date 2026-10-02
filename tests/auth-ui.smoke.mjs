import { chromium } from 'playwright';
import assert from 'node:assert/strict';
const browser = await chromium.launch({channel:'msedge', headless:true});
const base = process.env.SEAROUTE_TEST_URL || 'http://localhost:5018';
try {
 const page = await browser.newPage();
 const errors=[]; page.on('pageerror', e=>errors.push(e.message));
 for(const path of ['/Home/Index','/SignUp']) {
  await page.goto(base+path);
  await page.waitForFunction(()=> !document.getElementById('google-sign-in').disabled);
  await page.setViewportSize({width:390,height:844});
  assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth <= innerWidth),true);
  const popupEvent=page.waitForEvent('popup');
  await page.locator('#google-sign-in').click();
  const popup=await popupEvent;
  await popup.waitForURL(/accounts\.google\.com/, {timeout:30000});
  await popup.close();
  await page.waitForFunction(()=>document.querySelector('[role=status]').textContent.includes('closed'));
 }
 assert.deepEqual(errors,[]);
 assert.equal((await fetch(base+'/Home/Dashboard',{redirect:'manual'})).status,302);
 assert.equal((await fetch(base+'/Auth/Csrf',{headers:{Authorization:'Bearer forged-token'}})).status,401);
 assert.equal((await fetch(base+'/Auth/Logout',{method:'POST'})).status,400);
 console.log('PASS: both pages load Firebase, remain responsive, open Google account selection, handle cancellation, and reject unauthenticated dashboard / forged tokens.');
} finally { await browser.close(); }
