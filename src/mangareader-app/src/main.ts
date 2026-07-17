import App from './App.svelte';
import './app.css';

const target = document.getElementById('app');
if (!target) throw new Error('#app não encontrado no index.html');

const app = new App({ target });
export default app;
