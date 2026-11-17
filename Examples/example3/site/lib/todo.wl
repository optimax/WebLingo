// The to-do list, read from and written to its database: the one wasp.config names "todo", on SQL Server Express
// LocalDB. Each function opens the database, does its work, and closes it again at the end of its using; SqlClient keeps
// the connection in its pool for the next request.

import { Database } from "std:db";

export const priorities = [[1, "High"], [2, "Normal"], [3, "Low"]];

// "open", "done" or "all" from a query's text, "open" for anything else.
export function showOf(text) {
    return text == "done" || text == "all" ? text : "open";
}

// The id a text gives, or null when it is not one.
export function idOf(text) {
    try {
        return Integer.parse(text ?? "");
    } catch (e) {
        return null;
    }
}

// Each list, with how many items it has, and how many of them are still to do.
export async function lists() {
    using (var db = await Database.open("todo")) {
        return await db.query("""
            SELECT l.Id, l.Name, l.Colour, COUNT(i.Id) AS Total, SUM(CASE WHEN i.Done = 0 THEN 1 ELSE 0 END) AS [Open]
            FROM Lists l LEFT JOIN Items i ON i.ListId = l.Id
            GROUP BY l.Id, l.Name, l.Colour
            ORDER BY l.Name
            """);
    }
}

// How many items are still to do, how many of them are overdue, and how many are done.
export async function counts() {
    using (var db = await Database.open("todo")) {
        return (await db.query("""
            SELECT ISNULL(SUM(CASE WHEN Done = 0 THEN 1 ELSE 0 END), 0) AS [Open],
                   ISNULL(SUM(CASE WHEN Done = 0 AND Due < CAST(SYSDATETIME() AS DATE) THEN 1 ELSE 0 END), 0) AS Overdue,
                   ISNULL(SUM(CASE WHEN Done = 1 THEN 1 ELSE 0 END), 0) AS Done
            FROM Items
            """))[0];
    }
}

// The items to show: those still to do, those done, or all; of one list, or of all when list is null; and those whose
// title or notes hold a word, when search is not null. The soonest due first, those without a date after them.
export async function items(show, list, search) {
    using (var db = await Database.open("todo")) {
        return await db.query("""
            SELECT i.Id, i.Title, i.Notes, i.Priority, i.Due, i.Done, i.Completed, i.ListId, l.Name AS List, l.Colour,
                   CAST(CASE WHEN i.Done = 0 AND i.Due < CAST(SYSDATETIME() AS DATE) THEN 1 ELSE 0 END AS BIT) AS Overdue
            FROM Items i JOIN Lists l ON l.Id = i.ListId
            WHERE (@show = 'all' OR i.Done = CASE WHEN @show = 'done' THEN 1 ELSE 0 END)
              AND (@list IS NULL OR i.ListId = @list)
              AND (@search IS NULL OR i.Title LIKE '%' + @search + '%' OR i.Notes LIKE '%' + @search + '%')
            ORDER BY CASE WHEN i.Due IS NULL THEN 1 ELSE 0 END, i.Due, i.Priority, i.Title
            """, { show: show, list: list, search: search });
    }
}

// One item, by its id, or null when there is none.
export async function one(id) {
    using (var db = await Database.open("todo")) {
        return (await db.query("""
            SELECT i.Id, i.Title, i.Notes, i.Priority, i.Due, i.Done, i.Completed, i.Created, i.ListId, l.Name AS List
            FROM Items i JOIN Lists l ON l.Id = i.ListId
            WHERE i.Id = @id
            """, { id: id }))?[0];
    }
}

// A new item, its id the one SQL Server gives it, which OUTPUT returns.
export async function add(item) {
    using (var db = await Database.open("todo")) {
        return (await db.query("""
            INSERT INTO Items (ListId, Title, Notes, Priority, Due)
            OUTPUT INSERTED.Id
            VALUES (@list, @title, @notes, @priority, @due)
            """, { list: item["ListId"], title: item["Title"], notes: item["Notes"], priority: item["Priority"], due: item["Due"] }))[0]["Id"];
    }
}

// An item changed, done when it was not, or open again; the rows changed, 0 when there is no such item.
export async function update(id, item) {
    using (var db = await Database.open("todo")) {
        return await db.execute("""
            UPDATE Items
            SET ListId = @list, Title = @title, Notes = @notes, Priority = @priority, Due = @due, Done = @done,
                Completed = CASE WHEN @done = 0 THEN NULL WHEN Done = 0 THEN SYSDATETIME() ELSE Completed END
            WHERE Id = @id
            """, { id: id, list: item["ListId"], title: item["Title"], notes: item["Notes"], priority: item["Priority"],
                   due: item["Due"], done: item["Done"] });
    }
}

// An item marked done, or open again; the rows changed.
export async function toggle(id) {
    using (var db = await Database.open("todo")) {
        return await db.execute("""
            UPDATE Items
            SET Done = CASE WHEN Done = 1 THEN 0 ELSE 1 END,
                Completed = CASE WHEN Done = 1 THEN NULL ELSE SYSDATETIME() END
            WHERE Id = @id
            """, { id: id });
    }
}

// An item deleted; the rows changed.
export async function remove(id) {
    using (var db = await Database.open("todo")) {
        return await db.execute("DELETE FROM Items WHERE Id = @id", { id: id });
    }
}

// A form's fields read as an item, its keys a row's, and what is wrong with them, or null: a title of 1 to 200 letters,
// a list there is, a priority of 1 to 3, a date or none, notes of 1,000 letters at most or none.
export function read(form, lists) {
    var item = {
        Title: (form?["title"] ?? "").trim(),
        Notes: (form?["notes"] ?? "").trim(),
        ListId: idOf(form?["list"]),
        Priority: idOf(form?["priority"]) ?? 2,
        Due: null,
        Done: form?["done"] == "1"
    };
    if (item["Notes"] == "") {
        item["Notes"] = null;
    }
    var problem = null;
    var due = (form?["due"] ?? "").trim();
    if (due != "") {
        try {
            item["Due"] = DateTime.parse(due, "yyyy-MM-dd");
        } catch (e) {
            problem = "A date is written as 2026-11-30.";
        }
    }
    if (item["Title"] == "" || item["Title"].length > 200) {
        problem = "Say what is to be done, in 200 letters at most.";
    } else if (item["Notes"] != null && item["Notes"].length > 1000) {
        problem = "Notes are 1,000 letters at most.";
    } else if (!lists.any(l => l["Id"] == item["ListId"])) {
        problem = "Choose one of the lists.";
    } else if (item["Priority"] < 1 || item["Priority"] > 3) {
        problem = "Choose a priority.";
    }
    return { item: item, problem: problem };
}

// When an item is due, in words: today, tomorrow, yesterday, or its date; nothing when it has no date.
export function dueText(due) {
    if (due == null) {
        return "";
    }
    var today = DateTime.now();
    var day = due.format("yyyy-MM-dd");
    if (day == today.format("yyyy-MM-dd")) {
        return "Today";
    }
    if (day == today.addDays(1).format("yyyy-MM-dd")) {
        return "Tomorrow";
    }
    if (day == today.addDays(-1).format("yyyy-MM-dd")) {
        return "Yesterday";
    }
    return due.year == today.year ? due.format("ddd d MMM") : due.format("d MMM yyyy");
}

// A date as an <input type="date"> holds it, or nothing.
export function dateValue(due) {
    return due == null ? "" : due.format("yyyy-MM-dd");
}
