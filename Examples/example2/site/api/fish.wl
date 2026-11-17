// /api/fish: the catalogue as JSON, ?water=marine for one water's fish.

import { catalogue, waterOf } from "/lib/store.wl";

export async function get(request, response) {
    response.headers["Content-Type"] = "application/json";
    write(Json.stringify(await catalogue(waterOf(request.query?["water"]), null, null)));
}
