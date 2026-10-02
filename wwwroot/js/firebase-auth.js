import { auth } from './firebase-config.js';
import { GoogleAuthProvider, signInWithPopup, signInWithEmailAndPassword, createUserWithEmailAndPassword, updateProfile, setPersistence, browserLocalPersistence, browserSessionPersistence, signOut } from 'https://www.gstatic.com/firebasejs/12.19.0/firebase-auth.js';

const form = document.querySelector('#login-form, #signup-form');
const status = document.querySelector('#login-status, #signup-status');
const google = document.getElementById('google-sign-in');
const messages = {
    'auth/popup-closed-by-user': 'The Google window was closed. Please try again.',
    'auth/popup-blocked': 'Allow pop-ups for SeaRoute, then try again.',
    'auth/cancelled-popup-request': 'Please finish signing in in the Google window.',
    'auth/unauthorized-domain': 'Google sign-in is unavailable on this domain. Contact your administrator.',
    'auth/operation-not-allowed': 'This sign-in method is not enabled. Contact your administrator.',
    'auth/invalid-credential': 'Your email or password is incorrect.',
    'auth/email-already-in-use': 'An account already uses this email. Please sign in.',
    'auth/account-exists-with-different-credential': 'This email uses another sign-in method. Sign in using that method.',
    'auth/weak-password': 'Please choose a stronger password.',
    'auth/too-many-requests': 'Too many attempts. Please wait and try again.',
    'auth/network-request-failed': 'Check your connection and try again.'
};
function show(message) { status.textContent = message; status.hidden = false; }
function busy(value) {
    form.setAttribute('aria-busy', String(value));
    form.querySelector('button[type="submit"]').disabled = value;
    google.disabled = value;
}
async function session(user, rememberMe) {
    const token = await user.getIdToken(true);
    const headers = { Authorization: `Bearer ${token}` };
    const csrf = await fetch('/Auth/Csrf', { headers, credentials: 'same-origin', cache: 'no-store' });
    if (!csrf.ok) {
        const error = await csrf.json().catch(() => null);
        throw new Error(error?.message || 'We could not verify your account. Please try signing in again.');
    }
    const { token: csrfToken } = await csrf.json();
    const response = await fetch('/Auth/Session', {
        method: 'POST', credentials: 'same-origin',
        headers: { ...headers, 'Content-Type': 'application/json', 'X-CSRF-TOKEN': csrfToken },
        body: JSON.stringify({ rememberMe })
    });
    if (!response.ok) {
        const error = await response.json().catch(() => null);
        throw new Error(error?.message || 'We could not start your session. Please try signing in again.');
    }
    window.location.assign((await response.json()).redirectUrl);
}
if (form) {
    document.querySelectorAll('.password-toggle').forEach(toggle => {
        toggle.hidden = false;
        const input = document.getElementById(toggle.getAttribute('aria-controls'));
        toggle.addEventListener('click', () => {
            const showPassword = input.type === 'password';
            input.type = showPassword ? 'text' : 'password';
            toggle.textContent = showPassword ? 'Hide' : 'Show';
            toggle.setAttribute('aria-pressed', String(showPassword));
            toggle.setAttribute('aria-label', `${showPassword ? 'Hide' : 'Show'} password`);
        });
    });
    const confirmation = document.getElementById('confirm-password');
    const fullName = document.getElementById('full-name');
    function validate() {
        if (fullName) fullName.setCustomValidity(fullName.value.trim() ? '' : 'Please enter your full name.');
        if (confirmation) confirmation.setCustomValidity(confirmation.value !== form.elements.password.value ? 'Passwords do not match.' : '');
    }
    form.addEventListener('input', () => { validate(); status.hidden = true; });
    async function authenticate(useGoogle) {
        if (form.getAttribute('aria-busy') === 'true') return;
        if (!useGoogle) { validate(); if (!form.reportValidity()) return; }
        busy(true);
        show(useGoogle ? 'Choose your Google account in the sign-in window' : 'Signing you in');
        const rememberMe = form.elements.rememberMe?.checked ?? false;
        try {
            await setPersistence(auth, rememberMe ? browserLocalPersistence : browserSessionPersistence);
            let result;
            if (useGoogle) {
                const provider = new GoogleAuthProvider();
                provider.setCustomParameters({ prompt: 'select_account' });
                result = await signInWithPopup(auth, provider);
            } else if (form.id === 'signup-form') {
                result = await createUserWithEmailAndPassword(auth, form.elements.email.value.trim(), form.elements.password.value);
                await updateProfile(result.user, { displayName: fullName.value.trim() });
            } else {
                result = await signInWithEmailAndPassword(auth, form.elements.email.value.trim(), form.elements.password.value);
            }
            await session(result.user, rememberMe);
        } catch (error) {
            show(messages[error.code] || (error.code ? 'Unable to sign in. Please try again.' : error.message));
        } finally { busy(false); }
    }
    form.addEventListener('submit', event => { event.preventDefault(); authenticate(false); });
    google.addEventListener('click', () => authenticate(true));
    if (new URLSearchParams(location.search).get('signedOut')?.toLowerCase() === 'true') {
        try { await signOut(auth); }
        catch { show('Unable to clear your saved sign-in. Please reload and try again.'); }
    }
    busy(false);
}
const logout = document.getElementById('logout-form');
if (logout) {
    logout.addEventListener('submit', async event => {
        event.preventDefault();
        try { await signOut(auth); } finally { HTMLFormElement.prototype.submit.call(logout); }
    });
}
