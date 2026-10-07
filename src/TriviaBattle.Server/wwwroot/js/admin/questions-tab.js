// Questions tab: browse/search questions and edit them in a dialog.

const QuestionsTab = {
    categories: [],
    editing: null, // the question open in the editor; null = creating a new one

    init() {
        ['q-category', 'q-difficulty', 'q-inactive'].forEach(id =>
            document.getElementById(id).addEventListener('change', () => QuestionsTab.loadQuestions()));

        let searchTimer;
        document.getElementById('q-search').addEventListener('input', () => {
            clearTimeout(searchTimer);
            searchTimer = setTimeout(() => QuestionsTab.loadQuestions(), 300);
        });

        document.getElementById('q-new').addEventListener('click', () => QuestionsTab.openEditor(null));

        // Editor dialog
        document.getElementById('question-form').addEventListener('submit', event => {
            event.preventDefault();
            QuestionsTab.save();
        });
        document.getElementById('qf-cancel').addEventListener('click', () => document.getElementById('question-dialog').close());
        document.getElementById('qf-delete').addEventListener('click', () => QuestionsTab.remove());
        document.getElementById('qf-text').addEventListener('input', QuestionsTab.updateCharCount);
        document.getElementById('qf-media').addEventListener('input', QuestionsTab.updateMediaPreview);
        document.getElementById('qf-media-file').addEventListener('change', QuestionsTab.uploadMedia);
    },

    async load() {
        try {
            QuestionsTab.categories = await AdminApi.get('/api/admin/categories');
            QuestionsTab.fillCategorySelect(document.getElementById('q-category'), 'All categories');
            await QuestionsTab.loadQuestions();
        } catch (err) {
            showError(err);
        }
    },

    async loadQuestions() {
        const params = new URLSearchParams({ includeInactive: document.getElementById('q-inactive').checked });
        const category = document.getElementById('q-category').value;
        const difficulty = document.getElementById('q-difficulty').value;
        const search = document.getElementById('q-search').value.trim();
        if (category) params.set('categoryId', category);
        if (difficulty) params.set('difficulty', difficulty);
        if (search) params.set('search', search);

        const questions = await AdminApi.get(`/api/admin/questions?${params}`);
        document.getElementById('q-count').textContent = `${questions.length} question${questions.length === 1 ? '' : 's'}`;

        const tbody = document.getElementById('q-rows');
        tbody.innerHTML = questions.map(q => `
            <tr class="${q.isActive ? '' : 'is-inactive'}">
                <td>${UI.escape(q.text)}${q.mediaUrl ? ' &#128247;' : ''}</td>
                <td>${UI.escape(q.categoryName)}</td>
                <td>${q.difficulty}</td>
                <td>${UI.ANSWER_LETTERS[q.correctIndex]}: ${UI.escape(q.answers[q.correctIndex])}</td>
                <td>${q.isActive ? 'Active' : 'Off'}</td>
                <td><button data-id="${q.id}">Edit</button></td>
            </tr>`).join('');

        tbody.querySelectorAll('button[data-id]').forEach(button =>
            button.addEventListener('click', () => QuestionsTab.openEditor(questions.find(q => q.id === Number(button.dataset.id)))));
    },

    /** Keeps the current choice when the list of categories is refreshed. */
    fillCategorySelect(select, emptyLabel) {
        const current = select.value;
        select.innerHTML = (emptyLabel ? `<option value="">${emptyLabel}</option>` : '') +
            QuestionsTab.categories.map(c => `<option value="${c.id}">${UI.escape(c.name)}${c.isActive ? '' : ' (inactive)'}</option>`).join('');
        select.value = current;
    },

    // ------------------------------------------------------------------ Editor

    openEditor(question) {
        QuestionsTab.editing = question;
        const filterCategory = document.getElementById('q-category').value;

        document.getElementById('qf-title').textContent = question ? 'Edit question' : 'New question';
        QuestionsTab.fillCategorySelect(document.getElementById('qf-category'));
        document.getElementById('qf-category').value = question?.categoryId ?? (filterCategory || QuestionsTab.categories[0]?.id || '');
        document.getElementById('qf-difficulty').value = question?.difficulty ?? (document.getElementById('q-difficulty').value || 'Medium');
        document.getElementById('qf-active').checked = question?.isActive ?? true;
        document.getElementById('qf-text').value = question?.text ?? '';
        document.getElementById('qf-media').value = question?.mediaUrl ?? '';
        document.getElementById('qf-media-file').value = '';
        document.getElementById('qf-delete').hidden = !question;
        document.getElementById('qf-problems').textContent = '';

        document.getElementById('qf-answers').innerHTML = UI.ANSWER_LETTERS.map((letter, i) => `
            <div class="answer-row" data-letter="${letter}">
                <input type="radio" name="qf-correct" value="${i}" ${question?.correctIndex === i ? 'checked' : ''} aria-label="${letter} is correct">
                <span class="answer-letter">${letter}</span>
                <input type="text" id="qf-answer-${i}" maxlength="80" value="${UI.escape(question?.answers[i] ?? '')}">
            </div>`).join('');

        QuestionsTab.updateCharCount();
        QuestionsTab.updateMediaPreview();
        document.getElementById('question-dialog').showModal();
    },

    async save() {
        const correct = document.querySelector('input[name="qf-correct"]:checked');
        const request = {
            categoryId: Number(document.getElementById('qf-category').value),
            difficulty: document.getElementById('qf-difficulty').value,
            text: document.getElementById('qf-text').value,
            answers: [0, 1, 2, 3].map(i => document.getElementById(`qf-answer-${i}`).value),
            correctIndex: correct ? Number(correct.value) : -1,
            mediaUrl: document.getElementById('qf-media').value || null,
            isActive: document.getElementById('qf-active').checked,
        };

        try {
            if (QuestionsTab.editing) await AdminApi.put(`/api/admin/questions/${QuestionsTab.editing.id}`, request);
            else await AdminApi.post('/api/admin/questions', request);
            document.getElementById('question-dialog').close();
            await QuestionsTab.loadQuestions();
        } catch (err) {
            document.getElementById('qf-problems').textContent = err.message;
        }
    },

    async remove() {
        if (!confirm('Delete this question for good? (To stop using it but keep it, untick "Active" instead.)')) return;
        try {
            await AdminApi.del(`/api/admin/questions/${QuestionsTab.editing.id}`);
            document.getElementById('question-dialog').close();
            await QuestionsTab.loadQuestions();
        } catch (err) {
            document.getElementById('qf-problems').textContent = err.message;
        }
    },

    async uploadMedia(event) {
        const file = event.target.files[0];
        if (!file) return;
        try {
            const { url } = await AdminApi.upload('/api/admin/transfer/media', file);
            document.getElementById('qf-media').value = url;
            QuestionsTab.updateMediaPreview();
        } catch (err) {
            document.getElementById('qf-problems').textContent = err.message;
        }
    },

    updateCharCount() {
        const length = document.getElementById('qf-text').value.length;
        document.getElementById('qf-count').textContent = `(${length}/250)`;
    },

    updateMediaPreview() {
        const url = document.getElementById('qf-media').value.trim();
        const isVideo = /\.(mp4|webm)$/i.test(url);
        document.getElementById('qf-media-preview').innerHTML = !url ? ''
            : isVideo ? `<video src="${UI.escape(url)}" muted controls></video>`
            : `<img src="${UI.escape(url)}" alt="Question picture">`;
    },
};
