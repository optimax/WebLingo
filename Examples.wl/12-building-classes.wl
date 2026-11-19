// 12-building-classes.wl: classes built while a program runs, by the same means a declaration uses.
//
// A program can make a class, name it, and give it a constructor, methods, properties and constants, then use it as
// any declared class. There is no eval: code comes only from source files, and a class is built from functions.

var Point = new Class("Point");
Point.setMethod("constructor", function (this, x, y) { this.x = x; this.y = y; });
Point.setMethod("scaled", function (this, k) { return new Point(this.x * k, this.y * k); });
Point.setProperty("sum", function (this) { return this.x + this.y; }, null);
Point.setConstant("dimensions", 2);

var p = new Point(3, 4);
writeLn([p, p.scaled(2), p.sum, Point.dimensions, Point.__className, p is Point]);

// Any class may get methods later, a declared one too, and lose them again.
class Tea { constructor(name) { this.name = name; } }
Tea.setMethod("brew", function (this) { return $"brewing {this.name}"; });
writeLn(new Tea("sencha").brew());
Tea.removeMethod("brew");
try { new Tea("assam").brew(); } catch (MemberError e) { writeLn(e.message); }

// A class extending Class is a metaclass: its instances are classes, with its methods as their own static members.
class Entity extends Class {
  describe() { return $"entity {this.__className}"; }
}
var User = new Entity("User");
writeLn([User.describe(), User is Entity, new User() is User]);

/* Output:
[Point {x: 3, y: 4}, Point {x: 6, y: 8}, 7, 2, "Point", true]
brewing sencha
An instance of Tea has no member 'brew'.
["entity User", true, true]
*/
