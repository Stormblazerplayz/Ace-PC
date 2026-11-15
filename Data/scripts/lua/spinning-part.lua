--[[
	SPINNING PART
	Spins a part around and around (great for spinning obstacles or kill bars).

	HOW TO USE:
	1. Make a Part (a long thin one makes a cool spinner).
	2. Insert a Script into the Part and paste this in.
	3. Change SPIN_AXIS and DEGREES_PER_SECOND if you want.
	The script anchors the part for you.
]]

local RunService = game:GetService("RunService")

local part = script.Parent

-- SETTINGS
local DEGREES_PER_SECOND = 90 -- how fast it spins (360 = one full turn every second)
local SPIN_AXIS = "Y" -- "Y" = spin like a ceiling fan, "X" or "Z" = spin like a wheel

part.Anchored = true

-- Turn the axis letter into a direction
local localAxis
if SPIN_AXIS == "X" then
	localAxis = Vector3.new(1, 0, 0)
elseif SPIN_AXIS == "Z" then
	localAxis = Vector3.new(0, 0, 1)
else
	localAxis = Vector3.new(0, 1, 0)
end

local radiansPerSecond = math.rad(DEGREES_PER_SECOND)

RunService.Heartbeat:Connect(function(deltaTime)
	-- Turn a tiny bit every frame
	local turn = CFrame.fromAxisAngle(localAxis, radiansPerSecond * deltaTime)
	part.CFrame = part.CFrame * turn

	-- Tell physics the part is spinning, so players standing on it get carried around
	part.AssemblyAngularVelocity = part.CFrame:VectorToWorldSpace(localAxis) * radiansPerSecond
end)
