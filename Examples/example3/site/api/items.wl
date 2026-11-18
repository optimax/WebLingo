// /api/items: the items as JSON, those still to do; ?show=done or ?show=all, and ?list=2 for one list's.

import { Json } from "std:json";
import { items, showOf, idOf } from "/lib/todo.wl";

export async function get(request, response) {
    response.headers["Content-Type"] = "application/json";
    write(Json.stringify(await items(showOf(request.query?["show"]), idOf(request.query?["list"]), null)));
}
