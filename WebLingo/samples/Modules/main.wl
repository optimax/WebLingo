// A program of two files (specification, section 3.9): main.wl imports lib/greeting.wl.
import { writeLn } from "std:io";
import { greet, count } from "lib/greeting";
writeLn(greet("WebLingo"));
writeLn(greet("modules"));
writeLn(count.toString() + " greetings");
