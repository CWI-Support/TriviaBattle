// Import / Export tab: question packs as CSV or JSON.

const TransferTab = {
    init() {
        document.getElementById('t-export-csv').addEventListener('click', () =>
            AdminApi.download('/api/admin/transfer/export.csv').catch(showError));
        document.getElementById('t-export-json').addEventListener('click', () =>
            AdminApi.download('/api/admin/transfer/export.json').catch(showError));
        document.getElementById('t-import').addEventListener('click', TransferTab.import);
    },

    load() {},

    async import() {
        const file = document.getElementById('t-file').files[0];
        const resultBox = document.getElementById('t-result');
        if (!file) {
            resultBox.innerHTML = '<p class="problems">Choose a .csv or .json file first.</p>';
            return;
        }

        resultBox.innerHTML = '<p class="dim">Importing&hellip;</p>';
        try {
            const result = await AdminApi.upload('/api/admin/transfer/import', file);
            resultBox.innerHTML = `<p><span class="status-pill ok">Imported</span>
                ${result.added} question${result.added === 1 ? '' : 's'} added,
                ${result.skipped} already existed and ${result.skipped === 1 ? 'was' : 'were'} skipped.</p>`;
            document.getElementById('t-file').value = '';
        } catch (err) {
            const problems = err.problems || [err.message]; // e.g. "Line 4: difficulty must be Easy, Medium or Hard"
            resultBox.innerHTML = `<p><span class="status-pill bad">Nothing imported</span> Fix these and try again:</p>
                <ul class="problems import-problems">${problems.map(p => `<li>${UI.escape(p)}</li>`).join('')}</ul>`;
        }
    },
};
