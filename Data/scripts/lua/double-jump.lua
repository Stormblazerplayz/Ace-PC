--[[
	DOUBLE JUMP
	Press jump again while you are in the air to jump one more time!

	HOW TO USE:
	1. Put this LocalScript in StarterPlayer > StarterCharacterScripts.
	   (It goes into your character every time you spawn.)
	Works on keyboard (Space), phone (jump button) and controller (A).
]]

local UserInputService = game:GetService("UserInputService")

local character = script.Parent
local humanoid = character:WaitForChild("Humanoid")

-- SETTINGS
local EXTRA_JUMPS = 1 -- 1 = double jump, 2 = triple jump
local TIME_BEFORE_AIR_JUMP = 0.15 -- small wait so one press doesn't use two jumps

local jumpsUsed = 0
local canAirJump = false

local function isInAir()
	local state = humanoid:GetState()
	return state == Enum.HumanoidStateType.Freefall or state == Enum.HumanoidStateType.Jumping
end

-- Turns on the air jump a moment later, but only if we are STILL in the air
local function allowAirJumpSoon()
	task.delay(TIME_BEFORE_AIR_JUMP, function()
		if isInAir() then
			canAirJump = true
		end
	end)
end

humanoid.StateChanged:Connect(function(_oldState, newState)
	if newState == Enum.HumanoidStateType.Landed then
		-- Back on the ground: refill the extra jumps
		jumpsUsed = 0
		canAirJump = false
	elseif newState == Enum.HumanoidStateType.Freefall then
		-- In the air: allow an air jump after a tiny moment
		allowAirJumpSoon()
	end
end)

UserInputService.JumpRequest:Connect(function()
	if humanoid.Health <= 0 then
		return
	end
	if isInAir() and canAirJump and jumpsUsed < EXTRA_JUMPS then
		jumpsUsed += 1
		canAirJump = false
		humanoid:ChangeState(Enum.HumanoidStateType.Jumping)
		-- Allow the next air jump (for triple jump) after a moment
		allowAirJumpSoon()
	end
end)
