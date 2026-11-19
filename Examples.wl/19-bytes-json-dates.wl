// 19-bytes-json-dates.wl: bytes, JSON, and dates and times.
//
// Each class is a standard module's, imported to be named; a value keeps its members without the import.

import { Bytes, BytesBuilder } from "std:bytes";
import { Json } from "std:json";
import { DateTime } from "std:datetime";

// Bytes: binary data, distinct from text, immutable.
var hello = Bytes.from("héllo");
writeLn([hello.length, hello[1], hello.text(), hello.toHex(), hello.toBase64(), Bytes.fromBase64("aGk=").text()]);
var builder = new BytesBuilder();
builder.add("tea");
builder.add(0x21);
writeLn([builder.toBytes(), Bytes.concat([hello.slice(0, 1), Bytes.from("i")]).text()]);

// JSON: read into maps, arrays and values, and written back; a class may choose its own JSON with toJson().
var order = Json.parse("{\"tea\": \"sencha\", \"cups\": 2, \"price\": 4.5, \"extras\": [\"lemon\"], \"paid\": null}");
writeLn([order["tea"], order["cups"] is Integer, order["price"] is Number, order["extras"][0], order["paid"]]);
class Cup {
  constructor(tea) { this.tea = tea; this.temperature = 80; }
  toJson() { return { tea: this.tea }; }
}
writeLn([Json.stringify([1, "two", true, null]), Json.stringify(new Cup("assam"))]);

// Dates: an instant to the millisecond, with its offset from UTC; formats are .NET's, in the invariant culture.
var shipped = DateTime.utc(2026, 11, 18, 14, 30);
var due = shipped.addDays(3).addHours(2);
writeLn([shipped, $"{due:yyyy-MM-dd HH:mm}", due.dayOfWeek, (due - shipped) / 3600000]);
writeLn([DateTime.parse("2026-11-18T08:00:00+01:00").toUtc(), shipped < due, DateTime.now() is DateTime]);

/* Output:
[6, 195, "héllo", "68c3a96c6c6f", "aMOpbGxv", "hi"]
[Bytes:[116, 101, 97, 33], "hi"]
["sencha", true, true, "lemon", null]
["[1,\"two\",true,null]", "{\"tea\":\"assam\"}"]
[DateTime:2026-11-18T14:30:00Z, "2026-11-21 16:30", 6, 74.0]
[DateTime:2026-11-18T07:00:00Z, true, true]
*/
