// missing-semicolon.wl: no automatic semicolon insertion.
// A statement ends with a semicolon, always: WebLingo never inserts one, so that where a line breaks never changes
// what the code means, as it can in JavaScript (return, then a value on the next line, returning nothing).

var tea = "sencha"
writeLn(tea);

/* Errors:
missing-semicolon.wl(5,19): error WL1002: A ';' is missing here.
*/
