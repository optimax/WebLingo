// let-is-reserved.wl: var and const declare; let is reserved, and declares nothing.
// var is block-scoped, as JavaScript's let is, so that one keyword is enough, and code written with let does not
// silently mean something else.

let cups = 2;

/* Errors:
let-is-reserved.wl(5,1): error WL1001: 'let' is not a declaration keyword in WebLingo: use 'var' for a variable, or 'const' for a constant.
*/
