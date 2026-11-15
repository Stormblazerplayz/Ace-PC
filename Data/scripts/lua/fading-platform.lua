--[[
	FADING / DISAPPEARING PLATFORM
	When someone steps on it, it slowly fades away, lets them fall,
	then comes back after a few seconds.

	HOW TO USE:
	1. Make a Part (make sure it is Anchored).
	2. Insert a Script into the Part and paste this in.
]]

local part = script.Parent

-- SETTINGS
local FADE_TIME = 1 -- seconds it takes to fade out
local GONE_TIME = 3 -- seconds it stays gone
local FADE_STEPS = 10 -- more steps = smoother fade

part.Anchored = true

local startTransparency = part.Transparency
local busy = false -- our debounce: true while the platform is fading or gone

local function onTouched(hit)
	if busy then
		return
	end

	-- Only react to characters (things with a Humanoid), not random parts
	local character = hit:FindFirstAncestorOfClass("Model")
	if not character or not character:FindFirstChildOfClass("Humanoid") then
		return
	end

	busy = true

	-- Fade out a little bit at a time
	for step = 1, FADE_STEPS do
		part.Transparency = startTransparency + (1 - startTransparency) * (step / FADE_STEPS)
		task.wait(FADE_TIME / FADE_STEPS)
	end

	-- Now you can fall through it
	part.CanCollide = false
	task.wait(GONE_TIME)

	-- Come back!
	part.Transparency = startTransparency
	part.CanCollide = true
	busy = false
end

part.Touched:Connect(onTouched)
