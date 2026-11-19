// 20-crypto.wl: hashes, HMAC, random bytes, and passwords, with std:crypto.
//
// The defaults are the safe ones: random bytes from the platform's secure generator, passwords hashed slowly with a
// salt, and secrets compared in a time that does not tell where they differ.

import crypto from "std:crypto";

writeLn(crypto.sha256("abc").toHex());
writeLn(crypto.hmac("sha256", "key", "The quick brown fox jumps over the lazy dog").toHex());

var token = crypto.randomBytes(32);
writeLn([token.length, token.toBase64(true).length]);   // 32 random bytes, as URL-safe Base64 text

// A password's hash names its algorithm, its iterations and its salt, so that it verifies after the defaults change.
// It takes a large part of a second by design; a site's page awaits the Async form, so that its worker serves others.
var stored = await crypto.hashPasswordAsync("correct horse battery staple");
writeLn(stored.startsWith("pbkdf2-sha256$600000$"));
writeLn([await crypto.verifyPasswordAsync("correct horse battery staple", stored),
  await crypto.verifyPasswordAsync("Tr0ub4dor&3", stored)]);

// Comparing secrets with == can tell an attacker, by its time, how much of a guess was right; crypto.equal cannot.
var expected = crypto.hmac("sha256", "key", "message");
writeLn([crypto.equal(expected, crypto.hmac("sha256", "key", "message")), crypto.equal(expected, token)]);

/* Output:
ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad
f7bc83f430538424b13298e6aa6fb143ef4d59a14946175997479dbc2d1a3cd8
[32, 43]
true
[true, false]
[true, false]
*/
