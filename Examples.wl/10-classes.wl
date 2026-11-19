// 10-classes.wl: classes, inheritance, properties, static members, operators and text.
//
// Single inheritance, no prototypes to tamper with, and every member public. An instance is equal only to itself
// unless its class says otherwise, by overriding equal().

class Shape {
  constructor(name) { this.name = name; }
  area() { return 0.0; }
  describe() { return $"{this.name} of area {this.area():F2}"; }
  toString() { return $"<{this.describe()}>"; }
}

class Rectangle extends Shape {
  constructor(width, height) {
    super("rectangle");
    this.width = width;
    this.height = height;
  }
  area() { return this.width * this.height; }

  // A property, with a getter and a setter, and a field of its own, as in C# 14.
  property scale {
    get { return field ?? 1.0; }
    set(value) {
      if (value <= 0) throw new Error("A scale must be positive.");
      this.width *= value / this.scale;
      this.height *= value / this.scale;
      field = value;
    }
  }
}

class Circle extends Shape {
  static var made = 0;                    // a static field, of the class itself
  const PI = 3.14159;                     // a constant, read through the class or an instance
  constructor(radius) { super("circle"); this.radius = radius; Circle.made += 1; }
  area() { return Circle.PI * this.radius * this.radius; }
  static unit() { return new Circle(1.0); }
}

var shapes = [new Rectangle(2.0, 3.0), new Circle(1.0), Circle.unit()];
foreach (var shape in shapes) writeLn(shape.describe());
writeLn([Circle.made, shapes[0] is Shape, shapes[0] is Circle, shapes[1]]);

var r = shapes[0];
r.scale = 2.0;
writeLn([r.width, r.height, r.scale]);
try { r.scale = -1.0; } catch (e) { writeLn(e.message); }

// A method taken without a call stays bound to its instance.
var describeCircle = shapes[1].describe;
writeLn(describeCircle());

// Operators, by static methods, and equality by equal(), which == leaves alone.
class Money {
  constructor(cents) { this.cents = cents; }
  static operator +(a, b) { return new Money(a.cents + b.cents); }
  static operator <(a, b) { return a.cents < b.cents; }
  equal(other) { return other is Money && other.cents == this.cents; }
  toString() { return $"${new Decimal(this.cents) / 100m:F2}"; }   // decimals, exact for money
}
var lunch = new Money(1250) + new Money(375);
writeLn([lunch, lunch < new Money(2000), lunch == new Money(1625), lunch.equal(new Money(1625))]);

// write shows an instance as its class and fields; x?.name gives null where there is no such member.
class Point { constructor(x, y) { this.x = x; this.y = y; } }
writeLn([new Point(1, 2), new Point(1, 2)?.z, null?.x]);

/* Output:
rectangle of area 6.00
circle of area 3.14
circle of area 3.14
[2, true, false, <circle of area 3.14>]
[4.0, 6.0, 2.0]
A scale must be positive.
circle of area 3.14
[$16.25, true, false, true]
[Point {x: 1, y: 2}, null, null]
*/
