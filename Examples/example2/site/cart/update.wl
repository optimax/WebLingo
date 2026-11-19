// /cart/update, posted by the cart's form: each fish's quantity, a field named quantity-<id>, set at once, 0 taking the
// fish out; then the browser sent back to its cart. One form's fields, read as a map, many changes.

import { cartOf, setQuantity, refuse, toCart } from "/lib/cart.wl";
import { idOf } from "/lib/store.wl";

export async function post(request, response) {
    var cart = cartOf(request);
    var form = await request.form();
    var changes = [];
    foreach (var (name, value) in form) {
        if (!name.startsWith("quantity-")) {
            continue;
        }
        var fish = idOf(name.removePrefix("quantity-"));
        var quantity = idOf(value);
        if (fish == null || quantity == null || quantity < 0 || quantity > 100) {
            refuse(response, "A cart takes 0 to 100 of a fish, 0 taking it out.");
            return;
        }
        changes.push([fish, quantity]);
    }
    // Nothing is changed unless every field is right, and nothing without a cart to change.
    if (cart != null) {
        foreach (var change in changes) {
            await setQuantity(cart, change[0], change[1]);
        }
    }
    toCart(response);
}
