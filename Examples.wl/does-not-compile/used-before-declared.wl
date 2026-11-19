// used-before-declared.wl: a name means its variable only from its declaration on.
// Before it, the name means what it means outside the block, here nothing, so that the mistake is found before the
// script runs, not by reading an undefined value.

writeLn(cups);
var cups = 2;

/* Errors:
used-before-declared.wl(5,9): error WL2001: 'cups' is not defined.
*/
