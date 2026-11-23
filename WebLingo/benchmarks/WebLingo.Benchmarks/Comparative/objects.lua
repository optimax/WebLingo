local Point = {}
Point.__index = Point
function Point.new(x, y) return setmetatable({ x = x, y = y }, Point) end
function Point:add(other) return Point.new(self.x + other.x, self.y + other.y) end
function Point:sum() return self.x + self.y end
local p = Point.new(0, 0)
local step = Point.new(1, 2)
local total = 0
for i = 1, 1000000 do
  p = p:add(step)
  total = total + p:sum() % 7
end
print(total)
