// A module: what it exports, its importers see; count, as it is when read.
export var count = 0;

export function greet(name) {
    count = count + 1;
    return "Hello, " + name + "!";
}
