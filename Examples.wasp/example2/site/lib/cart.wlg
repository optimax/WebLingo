// The shopping carts: each a browser's, by a random id its cookie keeps, the fish it holds in the database's CartItems,
// so that a cart outlives the page that fills it, and nothing but its id goes to the browser.

import { Database } from "std:db";
import { DateTime } from "std:datetime";
import { Regex } from "std:regex";
import crypto from "std:crypto";

// The cookie's name, how long a browser keeps it, and what an id of ours looks like: 16 random bytes, in hexadecimal.
const cookie = "cart";
const days = 30;
const anId = new Regex("^[0-9a-f]{32}$");

// The cart a request's cookie names, its id; null when it names none, or names one no cart of ours could have.
export function cartOf(request) {
    foreach (var part in (request.headers?["Cookie"] ?? "").split(";")) {
        var pair = part.trim();
        if (pair.startsWith(cookie + "=")) {
            var id = pair.removePrefix(cookie + "=");
            return anId.test(id) ? id : null;
        }
    }
    return null;
}

// The request's cart, or a new one, whose id the response gives the browser to keep: HttpOnly, so that no script on a
// page can read it, and SameSite=Lax, so that a form another site posts to ours does not carry it.
export function cartFor(request, response) {
    var id = cartOf(request);
    if (id == null) {
        id = crypto.randomBytes(16).toHex();
        response.headers.add("Set-Cookie", $"{cookie}={id}; Path=/; Max-Age={days * 24 * 60 * 60}; HttpOnly; SameSite=Lax");
    }
    return id;
}

// A cart's fish, each with how many and its price, in the order they were added. What they come to is left to the page,
// the decimal price times how many: SQLite's product would have no declared type, and come back as SQLite holds it, an
// integer or a binary number, not a decimal.
export async function linesOf(id) {
    if (id == null) {
        return [];
    }
    using (var db = await Database.open("fish")) {
        return await db.query("""
            SELECT f.Id, f.Name, f.Slug, f.Price, f.Stock, c.Quantity
            FROM CartItems c JOIN Fish f ON f.Id = c.FishId
            WHERE c.Cart = @cart
            ORDER BY c.Added, f.Name
            """, { cart: id });
    }
}

// How many fish a cart holds, for the header.
export async function countOf(id) {
    if (id == null) {
        return 0;
    }
    using (var db = await Database.open("fish")) {
        return (await db.query("SELECT COALESCE(SUM(Quantity), 0) AS Count FROM CartItems WHERE Cart = @cart", { cart: id }))[0]["Count"];
    }
}

// Adds fish to a cart, at most 100 of one, more of a fish it holds already added to them; false when there is no such fish.
export async function add(id, fish, quantity) {
    using (var db = await Database.open("fish")) {
        if ((await db.query("SELECT Id FROM Fish WHERE Id = @fish", { fish: fish })).length == 0) {
            return false;
        }
        await db.execute("""
            INSERT INTO CartItems (Cart, FishId, Quantity, Added) VALUES (@cart, @fish, @quantity, @added)
            ON CONFLICT (Cart, FishId) DO UPDATE SET Quantity = MIN(100, Quantity + excluded.Quantity)
            """, { cart: id, fish: fish, quantity: quantity, added: DateTime.now() });
        return true;
    }
}

// Sets how many of a fish a cart holds, 0 taking the fish out.
export async function setQuantity(id, fish, quantity) {
    using (var db = await Database.open("fish")) {
        if (quantity == 0) {
            await db.execute("DELETE FROM CartItems WHERE Cart = @cart AND FishId = @fish", { cart: id, fish: fish });
        } else {
            await db.execute("UPDATE CartItems SET Quantity = @quantity WHERE Cart = @cart AND FishId = @fish",
                { cart: id, fish: fish, quantity: quantity });
        }
    }
}

// Takes every fish out of a cart.
export async function empty(id) {
    using (var db = await Database.open("fish")) {
        await db.execute("DELETE FROM CartItems WHERE Cart = @cart", { cart: id });
    }
}

// Checks a cart out, in one transaction: each fish's stock checked and lowered, an order recorded for each, and the cart
// emptied; or, where a fish has fewer left than the cart holds, nothing at all, the transaction rolled back by the error,
// so that no order is ever half placed. Gives the cart's lines and their total.
export async function checkout(id, customer) {
    using (var db = await Database.open("fish")) {
        return await db.transaction(async () => {
            var lines = await db.query("""
                SELECT f.Id, f.Name, f.Price, f.Stock, c.Quantity
                FROM CartItems c JOIN Fish f ON f.Id = c.FishId
                WHERE c.Cart = @cart
                ORDER BY c.Added, f.Name
                """, { cart: id });
            if (lines.length == 0) {
                throw new Error("Your cart is empty.");
            }
            var placed = DateTime.now();
            var total = 0m;
            foreach (var line in lines) {
                if (line["Stock"] < line["Quantity"]) {
                    throw new Error($"We have only {line['Stock']} {line['Name']} left.");
                }
                await db.execute("UPDATE Fish SET Stock = Stock - @quantity WHERE Id = @id", { quantity: line["Quantity"], id: line["Id"] });
                await db.execute("INSERT INTO Orders (FishId, Quantity, Customer, Placed) VALUES (@id, @quantity, @customer, @placed)",
                    { id: line["Id"], quantity: line["Quantity"], customer: customer, placed: placed });
                total += line["Price"] * line["Quantity"];
            }
            await db.execute("DELETE FROM CartItems WHERE Cart = @cart", { cart: id });
            return { lines: lines, total: total };
        });
    }
}

// A form refused: 400 (Bad Request), and why, as text, for a form a browser posted that no page of ours would have.
export function refuse(response, why) {
    response.status = 400;
    response.headers["Content-Type"] = "text/plain; charset=utf-8";
    write(why);
}

// The browser sent on to the cart, 303 (See Other), its next request a GET, so that reloading the cart posts nothing again.
export function toCart(response) {
    response.status = 303;
    response.headers["Location"] = "/cart";
}
