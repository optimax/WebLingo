// wrong-argument-count.wl: a call gives a function the arguments it takes.
// Too few or too many is an error, found before the script runs where the function is known, and when it runs
// otherwise (see 08-functions.wl): never a missing argument read as undefined.

function brew(tea, minutes) { return $"{tea} for {minutes} min"; }
writeLn(brew("sencha"));

/* Errors:
wrong-argument-count.wl(6,9): error WL2006: 'brew' takes 2 arguments, not 1.
*/
