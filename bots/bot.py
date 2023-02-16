import json
import os
import time
import uuid
from io import BytesIO

import numpy as np
import requests
from PIL import Image
from pathlib import Path

from datetime import datetime

from selenium import webdriver
from selenium.webdriver.chrome.options import Options

from FaceRecognition.FaceRecognitionModuleLight import FaceRecognitionModuleLight
from configparser import ConfigParser

# Reading config
config = ConfigParser()
config.read("config.ini")

class API:
    def __init__(self, jitsiAPIlink):
        self.jitsiAPIlink = jitsiAPIlink
        self.token = ""

    def getUser(self, userID):
        headers = {"Authorization": "Bearer " + self.token}

        response = requests.get(self.jitsiAPIlink + "/v1/Users/Get?userID=" + userID, headers=headers, verify=False)
        return response

    def getMeeting(self, meetingID):
        headers = {"Authorization": "Bearer " + self.token}

        response = requests.get(self.jitsiAPIlink + "/v1/Meetings/Get?meetingID=" + str(meetingID), headers=headers,
                                verify=False)
        return response

    def addCamStatus(self, meetingID, userID, status, data, filename, filepath):
        headers = {"Authorization": "Bearer " + self.token}

        payload = {'json': data}
        files = {'file': (filename, open(filepath, 'rb'))}

        response = requests.post(self.jitsiAPIlink + "/v1/Meetings/AddCamStatus?meetingID=" + str(
            meetingID) + "&userID=" + userID + "&status=" + status, data=payload, files=files, headers=headers,
                                 verify=False)
        return response

    def login(self):
        headers = {'Content-type': 'application/json', 'Accept': 'text/plain'}
        data = {
            "userName": config["BOT"]["login"],
            "password": config["BOT"]["password"]
        }
        response = requests.post(self.jitsiAPIlink + "/v1/Users/Login", data=json.dumps(data), headers=headers,
                                 verify=False)
        self.token = response.json()["token"]


class Bot:
    def checkFolder(self, folder):
        try:
            logsPath = os.path.join(str(Path(__file__).resolve().parent) + "/", folder)
        except:
            self.log("Error occured, on finding path")
            return

        if (not os.path.exists(folder)):
            try:
                os.mkdir(folder)
            except:
                self.log("Error occured while creating folder")
                return

    def log(self, text):
        with open(self.logPath, "a+") as logFile:
            logFile.write(text + "\n")


    #   Hiding elements
    def hide_element(self, el_className):
        js_string = "var element = document.getElementsByClassName(\"" + el_className + "\")[0].style.display=\"none\""
        try:
            self.driver.execute_script(js_string)
        except:
            pass
        time.sleep(0.5)

    def click_button(self, el_className):
        try:
            self.driver.find_element_by_id(el_className).click()
        except:
            pass
        time.sleep(0.5)

    def exit(self):
        self.exit_code = True

    def __init__(self, meetingID, delay, checkRepeat=0):
        #   Options of webdriver
        opt = Options()

        opt.add_argument("--disable-infobars")
        opt.add_argument("start-maximized")
        opt.add_argument("--disable-extensions")
        opt.add_argument('--headless')
        opt.add_argument('--window-size=1920,1080')
        opt.add_argument("--disable-popup-blocking")
        opt.add_argument('--no-sandbox')
        opt.add_argument('--disable-dev-shm-usage')

        opt.add_experimental_option("prefs", {
            "profile.default_content_setting_values.media_stream_mic": 1,
            "profile.default_content_setting_values.media_stream_camera": 1,
            "profile.default_content_setting_values.geolocation": 1,
            "profile.default_content_setting_values.notifications": 1

        })
        self.checkRepeat = checkRepeat
        self.meetingID = meetingID
        self.delay = delay
        self.exit_code = False
        self.logPath = os.path.join("logs", str(datetime.now().strftime("%d-%m-%Y-meeting-") + str(meetingID) + ".log"))
        self.pcf = FaceRecognitionModuleLight()
        self.allowedUsers = []

        self.checkFolder("logs")
        self.checkFolder("temp")

        try:
            self.API = API(config["SERVER"]["api_host"])
            self.API.login()
            self.meetingDetails = self.API.getMeeting(meetingID).json()
        except:
            self.log("Error occured while fetching meeting info")
            return

        try:
            #   Creating webdriver
            self.driver = webdriver.Chrome(executable_path=config["SERVER"]["chromedriver_path"], options=opt)
        except:
            self.log("Driver initialization error")
            return

        # move to config file
        jwt = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJjb250ZXh0Ijp7InVzZXIiOnsibmFtZSI6IkJvdCIsImVtYWlsIjoiQm90In19LCJtb2RlcmF0b3IiOmZhbHNlLCJhdWQiOiJsb2NhbGhvc3QiLCJpc3MiOiIyNzM3MTBkYWQtM2RmIiwic3ViIjoibG9jYWxob3N0Iiwicm9vbSI6IioifQ.9z1cs3stnQgwbYbPfLP_Z4YEl22YBG22-1172Wzyh4c"

        try:
            #   Joining meeting
            self.driver.get(config["SERVER"]["jitsi_host"] + "/" + self.meetingDetails[
                "name"].replace(" ", "") + '?jwt=' + jwt + '#config.startWithAudioMuted=true&config.startWithVideoMuted=true')
            time.sleep(3)
        except:
            self.log("Error occured, while joining the room")
            self.destroy()

        self.log("Connected to room: " + self.meetingDetails["name"])

        for user in self.meetingDetails["allowedUsers"]:
            allowedUser = self.API.getUser(user["userID"]).json()
            self.allowedUsers.append(allowedUser)

            self.log("Adding user " + allowedUser["fullName"])

            try:
                response = requests.get(allowedUser["avatarPath"])
                img = np.array(Image.open(BytesIO(response.content)))

                self.pcf.add_participant(allowedUser["id"], img)
            except:
                self.log("Avatar is not a face...")
                self.allowedUsers.remove(allowedUser)

    #   Screening loop
    def screen_loop(self):
        self.log("Starting screening loop with delay of: " + str(self.delay))

        while (not self.exit_code):
            while True:
                videocontainers = self.driver.find_elements_by_class_name("videocontainer")

                videocontainers_size = len(videocontainers)

                #   If bot is alone
                if (videocontainers_size == 3):
                    self.log("Waiting for users...")
                    time.sleep(10)
                else:
                    break

            #   For each element in videocontainers
            for i in range(2, videocontainers_size):
                #   0, 1 are indexes of fullscreen videocontainer and bot's videocontainer.
                #   Getting user's videocontainer element
                try:
                    videocontainer_of_user = self.driver.find_elements_by_class_name("videocontainer")[i]
                except:
                    self.log("Someone left the meeting")
                    break

                #   Getting user's name
                name_of_user = str(videocontainer_of_user.find_element_by_class_name("displayname").text)

                if (name_of_user == "" or name_of_user == "Bot"):
                    continue

                userID = ""
                self.log("Found user: " + name_of_user)
                for user in self.allowedUsers:
                    if user["fullName"] == name_of_user:
                        userID = user["id"]
                        break
                if userID == "":
                    self.log("User was not found in allowedUsers...")
                    continue
                self.log("userID is: " + str(userID))

                try:
                    #   Making user's videocontainer fullscreen
                    videocontainer_of_user.click()
                    time.sleep(0.5)
                except:
                    continue

                counter = 0
                result = tuple
                pic_bytes = None
                err = False
                while (counter < self.checkRepeat):
                    #   Taking screenshot of the user
                    self.log("Taking screenshot...")
                    try:
                        pic_bytes = BytesIO(self.driver.get_screenshot_as_png())
                        pic = np.array(Image.open(pic_bytes))
                    except:
                        self.log("Error occured, while taking screenshot")
                        err = True

                    self.log("Sending to model...")
                    try:
                        result = self.pcf.recognize(userID, pic[:, :, :3])

                        self.log(result[0])
                        if result[0] == "NOT_FOUND":
                            self.log("trying again...")
                            counter += 1
                            time.sleep(2)
                        else:
                            break
                    except:
                        self.log("Error occured, while recognizing the user")
                        err = True

                if (not err):
                    self.log("Adding camstatus to server...")

                    data = json.dumps([ob.dump() for ob in result[1]])
                    self.log(data)

                    pic_name = str(uuid.uuid4().hex) + ".jpg"
                    pic_path = os.path.join(self.tempPath, pic_name)

                    try:
                        image = (Image.open(pic_bytes)).convert('RGB')
                        image.save(pic_path)

                    except:
                        self.log("Error occured, while saving temp image")
                        err = True

                    if (not err):
                        response = self.API.addCamStatus(self.meetingID, userID, result[0], data, pic_name, pic_path)

                        if (response.status_code != 200):
                            print(response.content.decode("utf-8"))
                            self.log("Error occured, while adding camstatus to server")

                        try:
                            os.remove(pic_path)
                        except:
                            self.log("Error occured, while removing temp file")

                try:
                    #   Making user's videocontainer fullscreen
                    videocontainer_of_user.click()
                    time.sleep(0.5)
                except:
                    continue

            time.sleep(self.delay)

        self.destroy()

    def to_dict(self):
        return {"meetingID": self.meetingID, "delay": self.delay, "checkRepeat": self.checkRepeat}

    def destroy(self):
        self.log("Destroying the bot...")
        self.driver.close()
        self.driver.quit()
