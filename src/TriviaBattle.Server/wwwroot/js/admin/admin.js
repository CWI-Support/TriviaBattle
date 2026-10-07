// Admin page start-up: tab switching. Each tab is an object with load() in its own file
// (questions-tab.js, categories-tab.js, ...); load() is called every time the tab is opened,
// so what you see is always fresh from the server.

const ADMIN_TABS = {
    questions: QuestionsTab,
    categories: CategoriesTab,
    transfer: TransferTab,
    players: PlayersTab,
    live: LiveTab,
};

function openTab(name) {
    document.querySelectorAll('#tabs button').forEach(b => b.classList.toggle('is-active', b.dataset.tab === name));
    document.querySelectorAll('.tab').forEach(section => { section.hidden = section.id !== `tab-${name}`; });
    Object.entries(ADMIN_TABS).forEach(([tabName, tab]) => { if (tabName !== name) tab.leave?.(); });
    ADMIN_TABS[name].load();
    history.replaceState(null, '', `#${name}`);
}

document.querySelectorAll('#tabs button').forEach(button => {
    button.addEventListener('click', () => openTab(button.dataset.tab));
});

Object.values(ADMIN_TABS).forEach(tab => tab.init?.());
openTab(location.hash.slice(1) in ADMIN_TABS ? location.hash.slice(1) : 'questions');

/** Small helper used by all tabs: an alert for errors that aren't shown inline. */
function showError(err) {
    alert(err.message || err);
}

function formatDate(isoText) {
    return isoText ? new Date(isoText).toLocaleString() : '';
}
