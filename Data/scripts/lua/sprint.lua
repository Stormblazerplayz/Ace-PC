--[[
	SPRINT (hold Shift to run)
	Hold Left Shift to run faster. The camera zooms out a bit so it FEELS fast.
	On phones, a "Sprint" button appears on the screen.

	HOW TO USE:
	1. Put this LocalScript in StarterPlayer > StarterCharacterScripts.
]]

local ContextActionService = game:GetService("ContextActionService")
local TweenService = game:GetService("TweenService")

local character = script.Parent
local humanoid = character:WaitForChild("Humanoid")
local camera = workspace.CurrentCamera

-- SETTINGS
local SPRINT_SPEED = 28 -- running speed
local SPRINT_FOV = 80 -- camera zoom while running (normal is 70)

local normalSpeed = humanoid.WalkSpeed
local normalFOV = 70 -- Roblox's normal camera zoom
local fovTweenInfo = TweenInfo.new(0.25, Enum.EasingStyle.Quad, Enum.EasingDirection.Out)
local isSprinting = false

local function setSprinting(sprinting)
	if sprinting == isSprinting or humanoid.Health <= 0 then
		return
	end
	isSprinting = sprinting
	humanoid.WalkSpeed = sprinting and SPRINT_SPEED or normalSpeed
	local goalFOV = sprinting and SPRINT_FOV or normalFOV
	TweenService:Create(camera, fovTweenInfo, { FieldOfView = goalFOV }):Play()
end

local function onSprintAction(_actionName, inputState, _inputObject)
	if inputState == Enum.UserInputState.Begin then
		setSprinting(true)
	elseif inputState == Enum.UserInputState.End or inputState == Enum.UserInputState.Cancel then
		setSprinting(false)
	end
	-- "Pass" lets other things that use Shift (like Shift Lock) still work
	return Enum.ContextActionResult.Pass
end

-- true = also make an on-screen button for phones and tablets
ContextActionService:BindAction("Sprint", onSprintAction, true, Enum.KeyCode.LeftShift, Enum.KeyCode.ButtonL3)
ContextActionService:SetTitle("Sprint", "Run")

-- When you die, stop sprinting and put the camera back to normal
humanoid.Died:Connect(function()
	isSprinting = false
	camera.FieldOfView = normalFOV
	ContextActionService:UnbindAction("Sprint")
end)
