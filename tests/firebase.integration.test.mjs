import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { randomUUID } from 'node:crypto';
import { chromium } from 'playwright';
import pg from 'pg';
import { initializeApp, deleteApp } from 'firebase/app';
import { getAuth, signInWithEmailAndPassword, deleteUser } from 'firebase/auth';

// Opt-in live check: requires the running app, Admin ADC, PostgreSQL, and Edge.
// Temporary Firebase accounts and PostgreSQL Users rows are always cleaned up.
test('Firebase Authentication and PostgreSQL integration', {
    skip: process.env.SEAROUTE_LIVE_FIREBASE_TESTS !== '1', timeout: 180_000
}, async t => {
    const settings = JSON.parse(await readFile(new URL('../appsettings.json', import.meta.url))).Firebase;
    const base = process.env.SEAROUTE_TEST_URL || 'http://localhost:5018';
    const app = initializeApp({
        apiKey: settings.ApiKey, projectId: settings.ProjectId,
        appId: settings.AppId, authDomain: settings.AuthDomain
    }, 'integration-' + randomUUID());
    const auth = getAuth(app);
    const password = 'SeaRoute!' + randomUUID();
    const email = 'searoute-test-' + randomUUID() + '@example.com';
    // Node's PostgreSQL driver expects a URI. Otherwise use the same ignored
    // development .env host/port/database/user/password settings as the backend.
    let dbConfig;
    if (process.env.SEAROUTE_TEST_POSTGRES_URL) {
        dbConfig = { connectionString: process.env.SEAROUTE_TEST_POSTGRES_URL };
    } else {
        const text = await readFile(new URL('../.env', import.meta.url), 'utf8');
        const values = Object.fromEntries(text.split(/\r?\n/).filter(line => /^[a-zA-Z_]+=/u.test(line))
            .map(line => { const index = line.indexOf('='); return [line.slice(0, index), line.slice(index + 1)]; }));
        dbConfig = {
            host: values.host, port: Number(values.port || 5432), database: values.database,
            user: values.user, password: values.password,
            // Match Npgsql SSL modes: Require encrypts; VerifyCA/VerifyFull also validate certificates.
            ssl: values.sslmode?.toLowerCase() === 'disable' ? false : {
                rejectUnauthorized: ['verifyca', 'verifyfull', 'verify-ca', 'verify-full']
                    .includes(values.sslmode?.toLowerCase())
            }
        };
    }
    const db = new pg.Client(dbConfig);
    const users = [];
    let browser;
    t.after(async () => {
        await browser?.close();
        const failures = [];
        for (const user of users) {
            try {
                const account = await signInWithEmailAndPassword(auth, user.email, password);
                await deleteUser(account.user);
            } catch { failures.push('Temporary Firebase account cleanup failed.'); }
            try { await db.query('DELETE FROM "Users" WHERE "FirebaseUid" = $1', [user.uid]); }
            catch { failures.push('Temporary PostgreSQL row cleanup failed.'); }
        }
        await db.end();
        await deleteApp(app);
        assert.deepEqual(failures, []);
    });
    await db.connect();
    browser = await chromium.launch({ channel: 'msedge', headless: true });
    const page = await browser.newPage();
    const firestoreRequests = [];
    page.on('request', request => {
        if (new URL(request.url()).hostname.includes('firestore.googleapis.com')) firestoreRequests.push(request.url());
    });

    await t.test('anonymous, forged-token, and CSRF-less requests are rejected', async () => {
        assert.equal((await fetch(base + '/Home/Dashboard', { redirect: 'manual' })).status, 302);
        assert.equal((await fetch(base + '/Auth/Csrf')).status, 401);
        assert.equal((await fetch(base + '/Auth/Csrf', {
            headers: { Authorization: 'Bearer forged-token' }
        })).status, 401);
        assert.equal((await fetch(base + '/Auth/Logout', { method: 'POST' })).status, 400);
    });

    await t.test('signup ignores submitted identity fields and creates the verified UID in PostgreSQL', async () => {
        await page.route('**/Auth/Session', route => route.continue({ postData: JSON.stringify({
            ...route.request().postDataJSON(), firebaseUid: 'forged-owner', uid: 'forged-owner',
            email: 'forged@example.com', fullName: 'Forged profile'
        }) }));
        await page.goto(base + '/SignUp');
        await page.waitForFunction(() => !document.getElementById('create-account').disabled);
        await page.locator('#full-name').fill('SeaRoute Test User');
        await page.locator('#email').fill(email);
        await page.locator('#password').fill(password);
        await page.locator('#confirm-password').fill(password);
        await page.locator('#create-account').click();
        try { await page.waitForURL('**/Home/Dashboard', { timeout: 60_000 }); }
        finally {
            const state = await page.evaluate(async () => {
                const { getAuth } = await import('https://www.gstatic.com/firebasejs/12.19.0/firebase-auth.js');
                await getAuth().authStateReady();
                const user = getAuth().currentUser;
                return user ? { uid: user.uid, email: user.email } : null;
            });
            if (state) users.push(state);
        }
        await page.unroute('**/Auth/Session');
        assert.equal(await page.locator('#profile-name').textContent(), 'SeaRoute Test User');
        assert.equal(await page.locator('#profile-email').textContent(), email);
        const result = await db.query('SELECT "FirebaseUid", "Email", "FullName" FROM "Users" WHERE "FirebaseUid" = $1', [users[0].uid]);
        assert.deepEqual(result.rows, [{ FirebaseUid: users[0].uid, Email: email, FullName: 'SeaRoute Test User' }]);
        const cookie = (await page.context().cookies()).find(item => item.name === 'SeaRoute.Session');
        assert.ok(cookie?.httpOnly);
        assert.equal(cookie.sameSite, 'Lax');
    });

    await t.test('logout clears identities and repeated login retrieves the same PostgreSQL user', async () => {
        await page.locator('#logout-form button').click();
        await page.waitForURL(url => url.searchParams.get('signedOut')?.toLowerCase() === 'true');
        await page.waitForFunction(() => !document.getElementById('sign-in').disabled);
        await page.locator('#email').fill(email);
        await page.locator('#password').fill('incorrect-password');
        await page.locator('#sign-in').click();
        await page.waitForFunction(() => document.getElementById('login-status').textContent.includes('incorrect'));
        assert.equal((await page.context().cookies()).some(item => item.name === 'SeaRoute.Session'), false);
        await page.locator('#password').fill(password);
        await page.locator('[name="rememberMe"]').check();
        await page.locator('#sign-in').click();
        await page.waitForURL('**/Home/Dashboard');
        assert.equal(await page.locator('#profile-email').textContent(), email);
        const result = await db.query('SELECT COUNT(*)::int AS count FROM "Users" WHERE "FirebaseUid" = $1', [users[0].uid]);
        assert.equal(result.rows[0].count, 1);
        assert.deepEqual(firestoreRequests, []);
    });
});
