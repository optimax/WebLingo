// The store's catalogue, read from its database: the one wasp.config names "fish", a SQLite file outside the site.
// Each function opens the database, asks it, and closes it again, at the end of its using; SQLite's provider keeps the
// connection for the next request.

import { Database } from "std:db";

// "freshwater" or "marine" from a query's text, or null for anything else.
export function waterOf(text) {
    return text == "freshwater" || text == "marine" ? text : null;
}

export function heading(water) {
    return water switch { "freshwater" => "Freshwater fish", "marine" => "Marine fish", _ => "All our fish" };
}

// The fish of one water, or of both, up to a price, or whose names hold a word; each filter left out when it is null.
export async function catalogue(water, max, search) {
    using (var db = await Database.open("fish")) {
        return await db.query("""
            SELECT Id, Slug, Name, Species, Water, Price, Stock
            FROM Fish
            WHERE (@water IS NULL OR Water = @water)
              AND (@max IS NULL OR Price <= @max)
              AND (@search IS NULL OR Name LIKE '%' || @search || '%' OR Species LIKE '%' || @search || '%')
            ORDER BY Name
            """, { water: water, max: max, search: search });
    }
}

export async function featured() {
    using (var db = await Database.open("fish")) {
        return await db.query("""
            SELECT Id, Slug, Name, Species, Water, Price, Stock
            FROM Fish
            WHERE Slug IN ('betta', 'discus', 'clownfish')
            ORDER BY Price
            """);
    }
}

// Each water: how many kinds of fish, how many in the tanks, and the lowest price.
export async function waters() {
    using (var db = await Database.open("fish")) {
        return await db.query("""
            SELECT Water, COUNT(*) AS Kinds, SUM(Stock) AS InStock, MIN(Price) AS FromPrice
            FROM Fish
            GROUP BY Water
            ORDER BY Water
            """);
    }
}

// One fish, by its id, or null when there is none.
export async function one(id) {
    using (var db = await Database.open("fish")) {
        return (await db.query("SELECT * FROM Fish WHERE Id = @id", { id: id }))?[0];
    }
}

// The id a query's text gives, or null when it is not one.
export function idOf(text) {
    try {
        return Integer.parse(text ?? "");
    } catch (e) {
        return null;
    }
}

export async function recentOrders() {
    using (var db = await Database.open("fish")) {
        return await db.query("""
            SELECT o.Id, f.Id AS FishId, f.Name, o.Quantity, o.Customer, o.Placed, f.Price * o.Quantity AS Total
            FROM Orders o JOIN Fish f ON f.Id = o.FishId
            ORDER BY o.Id DESC
            LIMIT 20
            """);
    }
}

export async function credits() {
    using (var db = await Database.open("fish")) {
        return await db.query("SELECT Name, Slug, PhotoAuthor, PhotoLicence, PhotoSource FROM Fish ORDER BY Name");
    }
}
