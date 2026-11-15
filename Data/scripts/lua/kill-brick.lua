--[[
	KILL BRICK
	Anyone (player or NPC) who touches this part loses all their health.

	HOW TO USE:
	1. In Roblox Studio, make a Part (Model tab > Part).
	2. Right-click the Part > Insert Object > Script.
	3. Paste this whole script into that Script. Done!
]]

local part = script.Parent

-- SETTINGS (change these if you want)
local MAKE_IT_LOOK_DEADLY = true -- true = turn the part bright red and glowing

if MAKE_IT_LOOK_DEADLY then
	part.Color = Color3.fromRGB(255, 40, 40)
	part.Material = Enum.Material.Neon
end

local function onTouched(hit)
	-- "hit" is the body part that touched us (like a LeftFoot).
	-- The character is the Model that holds that body part.
	local character = hit:FindFirstAncestorOfClass("Model")
	if not character then
		return
	end

	local humanoid = character:FindFirstChildOfClass("Humanoid")

	-- Only kill if they are still alive. This check is also our "debounce":
	-- once health is 0, extra touches do nothing.
	if humanoid and humanoid.Health > 0 then
		humanoid.Health = 0
	end
end

part.Touched:Connect(onTouched)
