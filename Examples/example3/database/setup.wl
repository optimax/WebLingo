// Makes the to-do list's database on SQL Server Express LocalDB by running setup.sql, through the server weblingo.config
// names "server": its batches one by one, each ending with a line that says GO, as sqlcmd runs them, its :setvar giving
// the database's name, which database=Name changes. A relative path is from the current folder, so run it from this
// folder, where setup.sql is:
//
//   weblingo run setup.wl
//   weblingo run setup.wl database=OtherName

param database = null;
param script = "setup.sql";

import { Database } from "std:db";
import fs from "std:fs";

// The script's batches, and its scripting variables, read as sqlcmd reads them.
var variables = {};
var batches = [];
var lines = [];
foreach (var line in (await fs.readText(script)).split("\n")) {
    var trimmed = line.trim();
    if (trimmed.toUpper().startsWith(":SETVAR ")) {
        var words = trimmed.slice(8).trim().split(" ");
        variables[words[0]] = words[words.length - 1];
    } else if (trimmed.toUpper() == "GO") {
        batches.push("\n".join(lines));
        lines = [];
    } else {
        lines.push(line);
    }
}
batches.push("\n".join(lines));
if (database != null) {
    variables["Database"] = database;
}

// Each batch run on one connection, so that its USE holds for the batches after it; the last rows one gives are the
// script's summary.
var summary = null;
using (var db = await Database.open("server")) {
    foreach (var batch in batches) {
        var sql = batch;
        foreach (var (name, value) in variables) {
            sql = sql.replace($"$({name})", value);
        }
        if (sql.trim() != "") {
            var rows = await db.query(sql);
            if (rows.length > 0) {
                summary = rows[0];
            }
        }
    }
}
writeLn($"{variables['Database']} has {summary['Lists']} lists and {summary['Items']} items, {summary['Open']} of them open.");
