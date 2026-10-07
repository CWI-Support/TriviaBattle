// Calls to the admin API (/api/admin/...).
//
// If the server has an AdminApiKey set, the first call that gets "401 Unauthorized" asks the
// operator for the key, remembers it for this browser tab, and retries. With no key set on the
// server, nothing is ever asked.
//
// Every call throws an Error with a readable message if the server says no, so tabs can do:
//   try { await AdminApi.put(...) } catch (err) { showProblem(err.message) }

const AdminApi = {
    KEY_STORAGE: 'triviaAdminKey',

    get(url) { return AdminApi.request('GET', url); },
    post(url, body) { return AdminApi.request('POST', url, body); },
    put(url, body) { return AdminApi.request('PUT', url, body); },
    del(url) { return AdminApi.request('DELETE', url); },

    /** Uploads one file as multipart form data under the field name "file". */
    upload(url, file) {
        const form = new FormData();
        form.append('file', file);
        return AdminApi.request('POST', url, form);
    },

    /** Downloads a file (the browser's normal save), sending the admin key. */
    async download(url) {
        const response = await AdminApi.send('GET', url);
        const blob = await response.blob();
        const name = /filename="?([^";]+)"?/.exec(response.headers.get('Content-Disposition') || '')?.[1] || 'download';
        const link = document.createElement('a');
        link.href = URL.createObjectURL(blob);
        link.download = name;
        link.click();
        URL.revokeObjectURL(link.href);
    },

    async request(method, url, body) {
        const response = await AdminApi.send(method, url, body);
        if (response.status === 204) return null;
        return response.json();
    },

    // Sends the request, asking for the admin key and retrying if needed. Throws on errors.
    async send(method, url, body) {
        while (true) {
            const options = { method, headers: { 'X-Admin-Api-Key': sessionStorage.getItem(AdminApi.KEY_STORAGE) || '' } };
            if (body instanceof FormData) {
                options.body = body;
            } else if (body !== undefined) {
                options.headers['Content-Type'] = 'application/json';
                options.body = JSON.stringify(body);
            }

            const response = await fetch(url, options);

            if (response.status === 401) {
                const hadKey = !!sessionStorage.getItem(AdminApi.KEY_STORAGE);
                sessionStorage.setItem(AdminApi.KEY_STORAGE, await AdminApi.askForKey(hadKey ? 'That key was not accepted.' : ''));
                continue;
            }

            if (!response.ok) {
                const problems = await AdminApi.readProblems(response);
                const error = new Error(problems.join(' '));
                error.problems = problems; // the individual messages, for showing as a list
                throw error;
            }
            return response;
        }
    },

    /** The server's reasons for saying no, as a list of messages. */
    async readProblems(response) {
        try {
            const body = await response.json();
            if (body.problems) return body.problems;
            if (body.message) return [body.message];
            if (body.errors) return Object.values(body.errors).flat(); // ASP.NET's own validation errors
        } catch { /* not JSON */ }
        return [`The server said ${response.status} ${response.statusText}.`];
    },

    askForKey(error) {
        const dialog = document.getElementById('key-dialog');
        document.getElementById('key-error').textContent = error;
        dialog.showModal();
        return new Promise(resolve => {
            dialog.addEventListener('close', () => resolve(document.getElementById('key-input').value), { once: true });
        });
    },
};
