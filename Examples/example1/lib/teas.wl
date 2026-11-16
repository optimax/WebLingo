// The tea house's catalog: what each tea is, and the teas it sells.

class Tea {
    constructor(name, kind, origin, celsius, minutes, price) {
        this.name = name;
        this.kind = kind;
        this.origin = origin;
        this.celsius = celsius;
        this.minutes = minutes;
        this.price = price;
    }

    fahrenheit() {
        return this.celsius * 9 / 5 + 32;
    }
}

const teas = [
    new Tea("Sencha", "green", "Japan", 75, 1, 6.5),
    new Tea("Dragon Well", "green", "China", 80, 2, 8),
    new Tea("Darjeeling", "black", "India", 95, 3, 7.25),
    new Tea("Assam", "black", "India", 100, 4, 5.5),
    new Tea("Silver Needle", "white", "China", 85, 4, 12),
    new Tea("Tie Guan Yin", "oolong", "Taiwan", 90, 3, 9.75)
];

export function all() {
    return teas;
}

export function kinds() {
    return ["green", "black", "white", "oolong"];
}

export function ofKind(kind) {
    var found = [];
    foreach (var tea in teas) {
        if (tea.kind == kind) {
            found.push(tea);
        }
    }
    return found;
}

export function featured() {
    return [teas[0], teas[2], teas[4]];
}

export function steeping(minutes) {
    if (minutes == 1) {
        return "1 minute";
    }
    return $"{minutes} minutes";
}
