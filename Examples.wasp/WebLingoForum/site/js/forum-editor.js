// WebLingo Forum's Markdown fields with MarkdownEditor.Web, the owner's WYSIWYG editor (Documents/weblingo-forum.md,
// section 3.7): each <textarea data-markdown> replaced by a <markdown-editor>, which its form submits under the same
// name, as it submitted the textarea. Where scripts do not run, or the editor's files are not in /js/markdown-editor,
// which setup/copy-editor.wlg copies there, the textarea stays, and the form works as it did. A page without such a
// field loads nothing more.

const areas = document.querySelectorAll('textarea[data-markdown]');
if (areas.length > 0) {
    import('./markdown-editor/index.js').then(() => {
        for (const area of areas) {
            const editor = document.createElement('markdown-editor');
            editor.setAttribute('name', area.name);
            editor.setAttribute('value', area.value);
            editor.className = 'markdown';
            editor.style.height = area.rows > 6 ? '22rem' : '10rem';
            area.replaceWith(editor);
        }
    }, () => {
        // The editor is not there: the textareas stay.
    });
}
