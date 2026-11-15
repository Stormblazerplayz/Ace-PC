--[[
	TIMED BOMB
	Touch the bomb and it starts blinking: 3, 2, 1, BOOM!
	It hurts everyone close by, then comes back after a while.

	HOW TO USE:
	1. Make a Part (a black Ball looks like a bomb). Anchored is fine.
	2. Insert a Script into the Part and paste this in.
]]

local bomb = script.Parent

-- SETTINGS
local COUNTDOWN = 3 -- seconds from touch to explosion
local BLAST_RADIUS = 12 -- studs
local DAMAGE = 60 -- damage to anyone in range (100 = instant kill for normal players)
local FLING_POWER = 300000 -- how hard it throws people (0 = no throw)
local COME_BACK_TIME = 8 -- seconds until a new bomb appears

bomb.Anchored = true
local normalColor = bomb.Color
local normalTransparency = bomb.Transparency
local armed = false -- debounce: true while counting down or exploded

local function explode()
	local explosion = Instance.new("Explosion")
	explosion.Position = bomb.Position
	explosion.BlastRadius = BLAST_RADIUS
	explosion.BlastPressure = FLING_POWER
	explosion.DestroyJointRadiusPercent = 0 -- don't break your map apart; we do damage ourselves

	local alreadyHurt = {}
	explosion.Hit:Connect(function(part, _distance)
		local character = part:FindFirstAncestorOfClass("Model")
		local humanoid = character and character:FindFirstChildOfClass("Humanoid")
		if humanoid and not alreadyHurt[humanoid] then
			alreadyHurt[humanoid] = true
			humanoid:TakeDamage(DAMAGE)
		end
	end)

	explosion.Parent = workspace
end

bomb.Touched:Connect(function(hit)
	if armed then
		return
	end
	local character = hit:FindFirstAncestorOfClass("Model")
	if not character or not character:FindFirstChildOfClass("Humanoid") then
		return
	end
	armed = true

	-- Blink red and white, faster and faster
	local blinkTime = 0.4
	local timeLeft = COUNTDOWN
	local isRed = false
	while timeLeft > 0 do
		isRed = not isRed
		bomb.Color = isRed and Color3.fromRGB(255, 0, 0) or Color3.fromRGB(255, 255, 255)
		task.wait(blinkTime)
		timeLeft -= blinkTime
		blinkTime = math.max(0.08, blinkTime * 0.8)
	end

	explode()

	-- Hide the bomb, then bring it back
	bomb.Transparency = 1
	bomb.CanCollide = false
	bomb.CanTouch = false
	task.wait(COME_BACK_TIME)
	bomb.Color = normalColor
	bomb.Transparency = normalTransparency
	bomb.CanCollide = true
	bomb.CanTouch = true
	armed = false
end)
