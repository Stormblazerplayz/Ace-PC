--[[
	CONVEYOR BELT
	Anything standing on this part gets pushed along, like at an airport.

	HOW TO USE:
	1. Make a long flat Part.
	2. Insert a Script into the Part and paste this in.
	3. Things move toward the part's FRONT face. To flip the direction,
	   rotate the part 180 degrees, or make SPEED a negative number.
]]

local belt = script.Parent

-- SETTINGS
local SPEED = 15 -- studs per second (normal walking is 16)

belt.Anchored = true -- the belt itself stays still,

local function updateBelt()
	-- but we tell physics it's "moving", so things on top slide along.
	belt.AssemblyLinearVelocity = belt.CFrame.LookVector * SPEED
end

updateBelt()

-- If the belt gets turned (by you or another script), fix the direction
belt:GetPropertyChangedSignal("CFrame"):Connect(updateBelt)
