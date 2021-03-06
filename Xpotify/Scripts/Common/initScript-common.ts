﻿/// <reference path="uiElementModifier.ts" />
/// <reference path="dragDrop.ts" />
/// <reference path="../Lib/vibrant.ts" />
/// <reference path="../Lib/focus-visible.ts" />
/// <reference path="color.ts" />
/// <reference path="browserHistory.ts" />
/// <reference path="resize.ts" />
/// <reference path="startupAnimation.ts" />
/// <reference path="requestIntercepter.ts" />
/// <reference path="statusReport.ts" />
/// <reference path="action.ts" />
/// <reference path="pageTitleFinder.ts" />
/// <reference path="keyboardShortcutListener.ts" />
/// <reference path="mouseWheelListener.ts" />


namespace XpotifyScript.Common {

    declare var Xpotify: any;

    export function isProVersion(): boolean {
        //@ts-ignore
        return '{{XPOTIFYISPROVERSION}}' === '1';
    }

    export function getDeviceName(): string {
        return '{{DEVICENAME}}';
    }

    export function getAppName(): string {
        return isProVersion() ? 'Xpo Music Pro' : 'Xpo Music';
    }

    export function isLightTheme(): boolean {
        return (document.getElementsByTagName('body')[0].getAttribute('data-xpotifyTheme') === 'light');
    }

    export function init() {
        var errors = "";

        markPageAsInjected();
        initDragDrop();

        Xpotify.log("Initializing UiElemetModifier stuff...");
        errors += injectCss();
        errors += UiElementModifier.createPageTitle();
        errors += UiElementModifier.createBackButton();
        errors += UiElementModifier.createNavBarButtons();
        errors += UiElementModifier.createCompactOverlayButton();
        errors += UiElementModifier.addNowPlayingButton();
        errors += UiElementModifier.addBackgroundClass();
        errors += initNowPlayingBarCheck();

        Xpotify.log("Setting page hash and initializing resize and periodic checks...");
        setInitialPageHash();
        initOnResizeCheck();
        initPeriodicPageCheck();

        Xpotify.log("Initializing libraries...");
        Lib.FocusVisible.init();

        Xpotify.log("Initializing MouseWheelListener...");
        MouseWheelListener.init();

        Xpotify.log("Initializing KeyboardShortcutListener...");
        KeyboardShortcutListener.init();

        Xpotify.log("Initializing RequestIntercepter...");
        RequestIntercepter.startInterceptingFetch();

        Xpotify.log("Initializing StatusReport...");
        StatusReport.initRegularStatusReport();

        Xpotify.log("Initializing StartupAnimation...");
        StartupAnimation.init();

        // @ts-ignore
        if (window.XpotifyScript === undefined)
            // @ts-ignore
            window.XpotifyScript = XpotifyScript;

        Xpotify.log("Common.init() finished. errors = '" + errors + "'");
        return errors;
    }

    function markPageAsInjected() {
        var body = document.getElementsByTagName('body')[0];
        body.setAttribute('data-scriptinjection', '1');
    }

    function initDragDrop() {
        var body = document.getElementsByTagName('body')[0];
        body.ondrop = DragDrop.drop;
        body.ondragover = DragDrop.allowDrop;
    }

    function injectCss() {
        try {
            var css = '{{XPOTIFYCSSBASE64CONTENT}}';
            var style = document.createElement('style');
            document.getElementsByTagName('head')[0].appendChild(style);
            style.type = 'text/css';
            style.appendChild(document.createTextNode(atob(css)));
        }
        catch (ex) {
            return "injectCssFailed,";
        }
        return "";
    }

    function initNowPlayingBarCheck() {
        // Check and set now playing bar background color when now playing album art changes
        try {
            Lib.Vibrant.init();
            
            setInterval(function () {
                try {
                    var url = (<HTMLElement>document.querySelectorAll(".Root__now-playing-bar .now-playing .cover-art-image")[0]).style.backgroundImage.slice(5, -2);
                    var lightTheme = isLightTheme();

                    if (window["xpotifyNowPlayingIconUrl"] !== url || window["xpotifyNowPlayingLastSetLightTheme"] !== lightTheme) {
                        window["xpotifyNowPlayingIconUrl"] = url;
                        window["xpotifyNowPlayingLastSetLightTheme"] = lightTheme;

                        Color.setNowPlayingBarColor(url, lightTheme);
                    }
                }
                catch (ex) { }
            }, 1000);
        } catch (ex) {
            return "nowPlayingBarColorPollInitFailed,";
        }

        return "";
    }

    function setInitialPageHash() {
        setTimeout(function () {
            window.location.hash = "xpotifyInitialPage";

            setInterval(function () {
                var backButtonDivC = document.querySelectorAll(".backButtonContainer");
                if (backButtonDivC.length === 0) {
                    return;
                }
                var backButtonDiv = <HTMLElement>backButtonDivC[0];

                if (BrowserHistory.canGoBack()) {
                    backButtonDiv.classList.remove("backButtonContainer-disabled");
                } else {
                    backButtonDiv.classList.add("backButtonContainer-disabled");
                }
            }, 500);
        }, 1000);
    }

    function initOnResizeCheck() {
        window.addEventListener("resize", Resize.onResize, true);
        setInterval(Resize.onResize, 5000); // Sometimes an OnResize is necessary when users goes to a new page.
    }

    function periodicPageCheck() {
        try {
            if (document.querySelectorAll(".tracklist").length > 0) {
                TracklistExtended.initTracklistMod();
            }
        }
        catch (ex) {
            console.log(ex);
        }
    }

    function initPeriodicPageCheck() {
        setInterval(periodicPageCheck, 1000);
    }
}
