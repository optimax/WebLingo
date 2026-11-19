// declared-twice.wl: a name is declared once in a block; an inner block may shadow it.

var tea = "sencha";
var tea = "assam";

/* Errors:
declared-twice.wl(4,5): error WL2002: 'tea' is already declared in this scope.
*/
