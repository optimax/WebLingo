// /toggle: an item marked done, or to do again, by its checkbox on the list, a form posted with its id and the list's
// address; then the browser sent back there (303, See Other). A route handler, which writes no page of its own.

import { toggle, idOf } from "/lib/todo.wl";

export async function post(request, response) {
    var form = await request.form();
    var id = idOf(form?["id"]);
    var back = form?["back"] ?? "/";
    if (!back.startsWith("/") || back.startsWith("//")) {
        back = "/"; // Only an address on this site, never another's.
    }
    if (id == null || await toggle(id) == 0) {
        response.status = 404;
        response.headers["Content-Type"] = "text/plain; charset=utf-8";
        write("No such item.");
        return;
    }
    response.status = 303;
    response.headers["Location"] = back;
}
