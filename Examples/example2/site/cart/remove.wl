// /cart/remove, posted by a fish's Remove button in the cart's form, the button's own name and value, fish=<id>, being
// the field it sends: the fish taken out of the cart, then the browser sent back to it.

import { cartOf, setQuantity, refuse, toCart } from "/lib/cart.wl";
import { idOf } from "/lib/store.wl";

export async function post(request, response) {
    var cart = cartOf(request);
    var fish = idOf((await request.form())?["fish"]);
    if (fish == null) {
        refuse(response, "Which fish? None was named.");
        return;
    }
    if (cart != null) {
        await setQuantity(cart, fish, 0);
    }
    toCart(response);
}
