local total = 0
for i = 0, 299999 do
  local s = "item" .. tostring(i)
  if s < "item5" then total = total + #s else total = total + 1 end
  if string.find(s, "99", 1, true) then total = total + 2 end
end
print(total)
