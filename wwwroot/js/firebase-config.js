import { initializeApp } from 'https://www.gstatic.com/firebasejs/12.19.0/firebase-app.js';
import { getAuth } from 'https://www.gstatic.com/firebasejs/12.19.0/firebase-auth.js';

const element = document.getElementById('firebase-config');
if (!element) throw new Error('Firebase configuration is missing.');
const config = JSON.parse(element.textContent);
export const app = initializeApp(config);
export const auth = getAuth(app);
