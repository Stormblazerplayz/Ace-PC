--[[
	MOVING PART (goes back and forth forever)
	Uses TweenService to slide the part smoothly, then slide it back.

	HOW TO USE:
	1. Make a Part where you want the platform to START.
	2. Insert a Script into the Part and paste this in.
	3. Change MOVE_BY to choose how far and which way it moves.
	The script anchors the part for you (moving parts must be Anchored).
]]

local TweenService = game:GetService("TweenService")
local RunService = game:GetService("RunService")

local part = script.Parent

-- SETTINGS
local MOVE_BY = Vector3.new(0, 0, 20) -- how far to move in studs (X, Y, Z). Try (0, 10, 0) for an elevator!
local TRAVEL_TIME = 3 -- seconds to get from one end to the other
local PAUSE_TIME = 0.5 -- seconds to wait at each end

part.Anchored = true

local startCFrame = part.CFrame
local endCFrame = startCFrame + MOVE_BY

-- RepeatCount = -1 means "repeat forever", Reverses = true means "come back again"
local tweenInfo = TweenInfo.new(
	TRAVEL_TIME,
	Enum.EasingStyle.Sine,
	Enum.EasingDirection.InOut,
	-1,
	true,
	PAUSE_TIME
)

local tween = TweenService:Create(part, tweenInfo, { CFrame = endCFrame })
tween:Play()

-- BONUS: give the part a "speed" that matches how it moves.
-- This makes players standing on it ride along instead of sliding off.
local lastPosition = part.Position
RunService.Heartbeat:Connect(function(deltaTime)
	if deltaTime <= 0 then
		return
	end
	local newPosition = part.Position
	part.AssemblyLinearVelocity = (newPosition - lastPosition) / deltaTime
	lastPosition = newPosition
end)
