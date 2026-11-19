// missing-import.wl: a standard module's class is used only where it is imported.
// A script's imports list everything it depends on, and everything its host must grant it; the error says where
// the name is found.

writeLn(Json.stringify([1, 2]));

/* Errors:
missing-import.wl(5,9): error WL2001: 'Json' is not defined; did you mean Json, from std:json?
*/
