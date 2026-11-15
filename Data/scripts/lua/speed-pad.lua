--[[
	SPEED PAD
	Step on it to run super fast for a few seconds.

	HOW TO USE:
	1. Make a flat Part (Anchored) on the ground.
	2. Insert a Script into the Part and paste this in.
]]

local pad = script.Parent

-- SETTINGS
local BOOST_SPEED = 50 -- normal Roblox walking speed is 16
local BOOST_TIME = 3 -- seconds the boost lasts

pad.Anchored = true

local boostCount = {} -- [humanoid] = number, so a newer boost isn't ended by an older timer
local lastTouch = {} -- [humanoid] = time, our debounce

pad.Touched:Connect(function(hit)
	local character = hit:FindFirstAncestorOfClass("Model")
	if not character then
		return
	end
	local humanoid = character:FindFirstChildOfClass("Humanoid")
	if not humanoid or humanoid.Health <= 0 then
		return
	end

	-- Debounce: ignore touches that happen super close together
	local now = os.clock()
	if lastTouch[humanoid] and now - lastTouch[humanoid] < 0.25 then
		return
	end
	lastTouch[humanoid] = now

	-- Remember their normal speed the first time (stored on the Humanoid)
	if humanoid:GetAttribute("NormalSpeed") == nil then
		humanoid:SetAttribute("NormalSpeed", humanoid.WalkSpeed)
	end

	humanoid.WalkSpeed = BOOST_SPEED

	local myBoost = (boostCount[humanoid] or 0) + 1
	boostCount[humanoid] = myBoost

	task.delay(BOOST_TIME, function()
		-- Only end the boost if no newer boost started since
		if boostCount[humanoid] ~= myBoost then
			return
		end
		if humanoid.Parent then
			humanoid.WalkSpeed = humanoid:GetAttribute("NormalSpeed") or 16
			humanoid:SetAttribute("NormalSpeed", nil)
		end
		boostCount[humanoid] = nil
		lastTouch[humanoid] = nil
	end)
end)
