// constant-assigned.wl: a constant cannot be assigned.

const limit = 10;
limit = 20;

/* Errors:
constant-assigned.wl(4,1): error WL2003: 'limit' is a constant and cannot be assigned.
*/
