--[[
	RISING LAVA (that resets)
	The lava slowly rises. Touch it and you die. When it reaches the top,
	it waits a bit, then drops back down and starts rising again.

	HOW TO USE:
	1. Make a big flat Part at the BOTTOM of your map (this is the lava).
	2. Insert a Script into the lava Part and paste this in.
	3. Change RISE_HEIGHT to how high it should go.
]]

local TweenService = game:GetService("TweenService")

local lava = script.Parent

-- SETTINGS
local RISE_HEIGHT = 50 -- studs the lava rises
local RISE_TIME = 60 -- seconds to rise all the way
local WAIT_AT_TOP = 5 -- seconds it stays at the top
local WAIT_AT_BOTTOM = 10 -- seconds before it starts rising again
local CHECK_EVERY = 0.2 -- how often we look for players inside the lava

lava.Anchored = true
lava.CanCollide = false -- you sink INTO the lava (and die) instead of standing on it
lava.Color = Color3.fromRGB(255, 85, 0)
lava.Material = Enum.Material.Neon

local bottomCFrame = lava.CFrame
local topCFrame = bottomCFrame + Vector3.new(0, RISE_HEIGHT, 0)

-- Kill anything with a Humanoid that is inside the lava.
-- We check with GetPartsInPart because it works even when the lava moves into you.
local overlapParams = OverlapParams.new()
overlapParams.FilterType = Enum.RaycastFilterType.Exclude
overlapParams.FilterDescendantsInstances = { lava }

task.spawn(function()
	while true do
		for _, part in workspace:GetPartsInPart(lava, overlapParams) do
			local character = part:FindFirstAncestorOfClass("Model")
			local humanoid = character and character:FindFirstChildOfClass("Humanoid")
			if humanoid and humanoid.Health > 0 then
				humanoid.Health = 0
			end
		end
		task.wait(CHECK_EVERY)
	end
end)

-- The rise-and-reset loop
local riseInfo = TweenInfo.new(RISE_TIME, Enum.EasingStyle.Linear)
local dropInfo = TweenInfo.new(2, Enum.EasingStyle.Quad, Enum.EasingDirection.In)

while true do
	lava:SetAttribute("Status", "Waiting")
	task.wait(WAIT_AT_BOTTOM)

	lava:SetAttribute("Status", "Rising")
	local rise = TweenService:Create(lava, riseInfo, { CFrame = topCFrame })
	rise:Play()
	rise.Completed:Wait()

	lava:SetAttribute("Status", "Top")
	task.wait(WAIT_AT_TOP)

	lava:SetAttribute("Status", "Dropping")
	local drop = TweenService:Create(lava, dropInfo, { CFrame = bottomCFrame })
	drop:Play()
	drop.Completed:Wait()
end
