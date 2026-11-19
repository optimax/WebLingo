// implicit-global.wl: assigning a name never declares it.
// A typo in a name is an error before the script runs, not a new global variable created on the fly.

var count = 0;
cuont = count + 1;

/* Errors:
implicit-global.wl(5,1): error WL2001: 'cuont' is not declared: declare it first, with 'var cuont = …;'.
*/
