// Property access: instances made, their fields read and written, their methods called.
class Vector {
    constructor(x, y) {
        this.x = x;
        this.y = y;
    }
    add(other) {
        return new Vector(this.x + other.x, this.y + other.y);
    }
    lengthSquared() {
        return this.x * this.x + this.y * this.y;
    }
}
var v = new Vector(0, 0);
var step = new Vector(1, 2);
var total = 0;
for (var i = 0; i < 100000; i = i + 1) {
    v = v.add(step);
    total = total + v.lengthSquared();
}
writeLn(total);
