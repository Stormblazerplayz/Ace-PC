--[[
	OPENING DOOR (press E, click, or touch)
	The door slides up smoothly to open, then closes again by itself.

	HOW TO USE:
	1. Make a Part shaped like a door (Anchored).
	2. Insert a Script into the door Part and paste this in.
	3. Pick OPEN_MODE below:
	   "Prompt" = walk up and press E (or tap the button on phones)
	   "Click"  = click the door with your mouse
	   "Touch"  = it opens when you walk into it
]]

local TweenService = game:GetService("TweenService")
local Players = game:GetService("Players")

local door = script.Parent

-- SETTINGS
local OPEN_MODE = "Prompt" -- "Prompt", "Click" or "Touch"
local SLIDE_UP_BY = door.Size.Y -- how far it slides up (studs)
local SLIDE_TIME = 0.6 -- seconds to open or close
local AUTO_CLOSE_TIME = 3 -- seconds until it closes by itself (0 = stays open until used again)
-- (In "Touch" mode keep AUTO_CLOSE_TIME above 0, or the door will never close.)

door.Anchored = true

local closedCFrame = door.CFrame
local openCFrame = closedCFrame + Vector3.new(0, SLIDE_UP_BY, 0)
local tweenInfo = TweenInfo.new(SLIDE_TIME, Enum.EasingStyle.Quad, Enum.EasingDirection.Out)

local isOpen = false
local moving = false -- debounce: true while the door is sliding
local openNumber = 0 -- counts how many times we opened (so old auto-close timers are ignored)
local prompt = nil

local function setOpen(open)
	if moving or isOpen == open then
		return
	end
	moving = true
	isOpen = open

	local tween = TweenService:Create(door, tweenInfo, { CFrame = open and openCFrame or closedCFrame })
	tween:Play()
	tween.Completed:Wait()

	if prompt then
		prompt.ActionText = isOpen and "Close" or "Open"
	end
	moving = false
end

local function toggleDoor()
	if moving then
		return
	end
	if isOpen then
		setOpen(false)
	else
		openNumber += 1
		local thisOpen = openNumber
		setOpen(true)
		if AUTO_CLOSE_TIME > 0 then
			task.delay(AUTO_CLOSE_TIME, function()
				-- Only close if nobody closed and reopened it in the meantime
				if openNumber == thisOpen and isOpen then
					setOpen(false)
				end
			end)
		end
	end
end

if OPEN_MODE == "Prompt" then
	prompt = Instance.new("ProximityPrompt")
	prompt.ActionText = "Open"
	prompt.ObjectText = "Door"
	prompt.HoldDuration = 0 -- set to 1 to make people hold E for 1 second
	prompt.MaxActivationDistance = 8 + SLIDE_UP_BY -- extra reach, because the prompt rides up with the open door
	prompt.RequiresLineOfSight = false
	prompt.Parent = door
	prompt.Triggered:Connect(function(_player)
		toggleDoor()
	end)
elseif OPEN_MODE == "Click" then
	local clickDetector = Instance.new("ClickDetector")
	clickDetector.MaxActivationDistance = 20
	clickDetector.Parent = door
	clickDetector.MouseClick:Connect(function(_player)
		toggleDoor()
	end)
else
	-- "Touch": opens when a player walks into it
	door.Touched:Connect(function(hit)
		local character = hit:FindFirstAncestorOfClass("Model")
		if character and Players:GetPlayerFromCharacter(character) and not isOpen then
			toggleDoor()
		end
	end)
end
