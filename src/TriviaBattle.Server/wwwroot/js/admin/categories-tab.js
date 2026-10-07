// Categories tab: rename, switch on/off, add and delete categories.

const CategoriesTab = {
    init() {
        document.getElementById('c-add').addEventListener('click', async () => {
            const input = document.getElementById('c-new-name');
            try {
                await AdminApi.post('/api/admin/categories', { name: input.value, isActive: true });
                input.value = '';
                await CategoriesTab.load();
            } catch (err) {
                showError(err);
            }
        });
    },

    async load() {
        try {
            const categories = await AdminApi.get('/api/admin/categories');
            const tbody = document.getElementById('c-rows');

            tbody.innerHTML = categories.map(c => `
                <tr data-id="${c.id}" class="${c.isActive ? '' : 'is-inactive'}">
                    <td><input class="c-name" value="${UI.escape(c.name)}" maxlength="40"></td>
                    <td><input class="c-active" type="checkbox" ${c.isActive ? 'checked' : ''}></td>
                    <td>${c.easy}</td>
                    <td>${c.medium}</td>
                    <td>${c.hard}</td>
                    <td>${c.totalQuestions}</td>
                    <td><button class="c-save">Save</button> <button class="c-delete danger">Delete</button></td>
                </tr>`).join('');

            tbody.querySelectorAll('tr').forEach(row => {
                const id = row.dataset.id;
                const category = categories.find(c => c.id === Number(id));
                row.querySelector('.c-save').addEventListener('click', () => CategoriesTab.save(id, row));
                row.querySelector('.c-delete').addEventListener('click', () => CategoriesTab.remove(category));
            });
        } catch (err) {
            showError(err);
        }
    },

    async save(id, row) {
        try {
            await AdminApi.put(`/api/admin/categories/${id}`, {
                name: row.querySelector('.c-name').value,
                isActive: row.querySelector('.c-active').checked,
            });
            await CategoriesTab.load();
        } catch (err) {
            showError(err);
        }
    },

    async remove(category) {
        const warning = `Delete "${category.name}" and all ${category.totalQuestions} of its questions? This can't be undone.\n\n` +
                        'Tip: untick "Active" instead to just hide it from the kiosk.';
        if (!confirm(warning)) return;
        try {
            await AdminApi.del(`/api/admin/categories/${category.id}`);
            await CategoriesTab.load();
        } catch (err) {
            showError(err);
        }
    },
};
