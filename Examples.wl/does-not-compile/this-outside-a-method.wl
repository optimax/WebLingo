// this-outside-a-method.wl: this exists only in a method or a constructor.
// A function that is no method has no this to be called with by accident; one that wants a receiver names it,
// function (this, …).

function describe() { return this.name; }

/* Errors:
this-outside-a-method.wl(5,30): error WL2009: 'this' is only allowed in a method or a constructor.
*/
