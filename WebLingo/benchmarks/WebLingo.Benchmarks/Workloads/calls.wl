// Recursive calls of a declared function: about 243,000 of them.
import { writeLn } from "std:system";
function fib(n) {
    if (n < 2) return n;
    return fib(n - 1) + fib(n - 2);
}
writeLn(fib(25));
