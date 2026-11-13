// Strings concatenated and compared: 200,000 new strings, which the heap's collector reclaims.
var count = 0;
for (var i = 0; i < 20000; i = i + 1) {
    var s = "";
    for (var j = 0; j < 10; j = j + 1) s = s + "ab";
    if (s == "abababababababababab") count = count + 1;
}
writeLn(count);
