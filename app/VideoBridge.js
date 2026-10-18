(function () {
    "use strict";

    // This bridge only controls the page's existing video. It has no native host access.
    if (window.__eaghBridge && window.__eaghBridge.version === 1) return;

    var focusEnabled = false;
    var focusedVideo = null;
    var markedAncestors = [];
    var styleElement = null;
    var refreshTimer = null;
    var lastError = null;
    var marker = "data-eagh-focus";
    var videoMarker = "data-eagh-video";
    var ancestorMarker = "data-eagh-ancestor";
    var mediaSelector = "video,bwp-video";

    function error(code, message) {
        lastError = { ok: false, error: code, message: message };
        return lastError;
    }

    function isVisible(video) {
        if (!video || !video.isConnected) return false;
        var rect = video.getBoundingClientRect();
        if (rect.width < 32 || rect.height < 24) return false;
        var style = window.getComputedStyle(video);
        return style.display !== "none" && style.visibility !== "hidden" && Number(style.opacity) !== 0;
    }

    function isControllable(video) {
        // Bilibili can use a bwp-video element for software-decoded HEVC. Only use
        // upgraded elements that actually expose the standard media controls.
        return typeof video.play === "function" && typeof video.pause === "function" &&
            typeof video.paused === "boolean" && typeof video.currentTime === "number";
    }

    function selectVideo() {
        if (focusEnabled && focusedVideo) {
            if (isControllable(focusedVideo) && isVisible(focusedVideo)) return focusedVideo;
            // A still-connected old player can become hidden during a quality or
            // episode change. Reveal the page before selecting its replacement.
            clearFocusMarkers();
        }
        var videos = Array.prototype.slice.call(document.querySelectorAll(mediaSelector));
        var best = null;
        var bestScore = -1;
        videos.forEach(function (video) {
            if (!isControllable(video) || !isVisible(video)) return;
            var rect = video.getBoundingClientRect();
            var area = Math.min(rect.width, window.innerWidth || rect.width) *
                Math.min(rect.height, window.innerHeight || rect.height);
            var score = area;
            if (video.closest && video.closest(".bpx-player-video-wrap,.bilibili-player-video-wrap,.bpx-player-container")) score += 1000000;
            if (!video.paused && !video.ended) score += 500000;
            if (video.readyState >= 2) score += 10000;
            if (score > bestScore) { bestScore = score; best = video; }
        });
        if (focusEnabled && best) applyFocus(best);
        return best;
    }

    function snapshot(video, command) {
        var duration = video && Number.isFinite(video.duration) ? video.duration : null;
        return {
            ok: true,
            command: command || "state",
            hasVideo: !!video,
            paused: video ? video.paused : true,
            currentTime: video && Number.isFinite(video.currentTime) ? video.currentTime : 0,
            duration: duration,
            aspectRatio: video && video.videoWidth > 0 && video.videoHeight > 0 ? video.videoWidth / video.videoHeight : 16 / 9,
            focused: focusEnabled,
            lastError: lastError
        };
    }

    function clearFocusMarkers() {
        if (focusedVideo) focusedVideo.removeAttribute(videoMarker);
        markedAncestors.forEach(function (element) { element.removeAttribute(ancestorMarker); });
        markedAncestors = [];
        focusedVideo = null;
        if (document.documentElement) document.documentElement.removeAttribute(marker);
        if (styleElement && styleElement.parentNode) styleElement.parentNode.removeChild(styleElement);
        styleElement = null;
    }

    function applyFocus(video) {
        clearFocusMarkers();
        if (!video) return;
        focusedVideo = video;
        video.setAttribute(videoMarker, "1");
        var ancestor = video.parentElement;
        while (ancestor && ancestor !== document.documentElement) {
            ancestor.setAttribute(ancestorMarker, "1");
            markedAncestors.push(ancestor);
            ancestor = ancestor.parentElement;
        }
        styleElement = document.createElement("style");
        styleElement.setAttribute("data-eagh-style", "1");
        styleElement.textContent =
            "html[" + marker + "]{overflow:hidden!important;background:#000!important;}" +
            "html[" + marker + "] body{overflow:hidden!important;background:#000!important;}" +
            "html[" + marker + "] body *{visibility:hidden!important;}" +
            "html[" + marker + "] [" + ancestorMarker + "]{transform:none!important;perspective:none!important;filter:none!important;contain:none!important;content-visibility:visible!important;will-change:auto!important;clip:auto!important;clip-path:none!important;overflow:visible!important;isolation:auto!important;opacity:1!important;}" +
            "html[" + marker + "] [" + videoMarker + "]{visibility:visible!important;display:block!important;position:fixed!important;inset:0!important;left:0!important;top:0!important;width:100vw!important;height:100vh!important;min-width:0!important;min-height:0!important;max-width:none!important;max-height:none!important;margin:0!important;padding:0!important;border:0!important;object-fit:contain!important;transform:none!important;opacity:1!important;z-index:2147483647!important;background:#000!important;}" +
            "html[" + marker + "] bwp-video[" + videoMarker + "] *{visibility:visible!important;}";
        (document.head || document.documentElement).appendChild(styleElement);
        document.documentElement.setAttribute(marker, "1");
    }

    function refreshFocus() {
        refreshTimer = null;
        if (!focusEnabled) return;
        // Remove our visibility rule before finding the replacement player after SPA navigation.
        clearFocusMarkers();
        var video = selectVideo();
        if (video) applyFocus(video);
    }

    function setFocus(enabled) {
        lastError = null;
        if (!enabled) {
            focusEnabled = false;
            clearFocusMarkers();
            return snapshot(selectVideo(), "setFocus");
        }
        var video = selectVideo();
        if (!video) return error("no_video", "当前页面没有可用的视频，请先打开并播放一个视频。");
        focusEnabled = true;
        applyFocus(video);
        return snapshot(video, "setFocus");
    }

    function seek(video, offset, command) {
        var current = Number(video.currentTime);
        if (!Number.isFinite(current)) return error("not_ready", "视频尚未准备好，请稍后再试。");
        var duration = Number(video.duration);
        var target = current + offset;
        if (Number.isFinite(duration) && duration >= 0) target = Math.max(0, Math.min(duration, target));
        var ranges = video.seekable;
        if (ranges && ranges.length > 0) {
            var nearest = null;
            var distance = Infinity;
            for (var i = 0; i < ranges.length; i++) {
                var start = ranges.start(i);
                var end = ranges.end(i);
                var candidate = Math.max(start, Math.min(end, target));
                var delta = Math.abs(candidate - target);
                if (delta < distance) { nearest = candidate; distance = delta; }
            }
            target = nearest;
        } else if (!Number.isFinite(duration) || duration <= 0) {
            return error("not_seekable", "当前视频还不能调整进度，或直播没有可回看的区间。");
        }
        if (!Number.isFinite(target)) return error("not_seekable", "当前视频没有可跳转的进度。");
        video.currentTime = target;
        return snapshot(video, command);
    }

    function execute(command) {
        if (command === "state") return snapshot(selectVideo(), command);
        lastError = null;
        var video = selectVideo();
        if (!video) return error("no_video", "当前页面没有可用的视频，请先打开并播放一个视频。");
        try {
            if (command === "togglePause" || command === "togglePlayback") {
                if (video.paused || video.ended) {
                    var playRequest = video.play();
                    if (playRequest && typeof playRequest.catch === "function") {
                        playRequest.catch(function () {
                            error("play_blocked", "网页阻止了自动播放，请点击网页的播放按钮；沉浸模式下按 8 返回网页。");
                        });
                    }
                } else video.pause();
                return snapshot(video, command);
            }
            if (command === "seekBackward") return seek(video, -5, command);
            if (command === "seekForward") return seek(video, 5, command);
            return error("unknown_command", "无法识别的视频操作。");
        } catch (exception) {
            return error("video_error", "视频操作失败，请检查网页播放器；沉浸模式下按 8 返回网页。");
        }
    }

    if (typeof MutationObserver !== "undefined") {
        new MutationObserver(function (records) {
            var mediaChanged = records.some(function (record) {
                return Array.prototype.some.call(record.addedNodes, containsVideo) ||
                    Array.prototype.some.call(record.removedNodes, containsVideo);
            });
            if (focusEnabled && mediaChanged && !refreshTimer) {
                refreshTimer = window.setTimeout(refreshFocus, 100);
            }
        }).observe(document.documentElement || document, { childList: true, subtree: true });
    }

    function containsVideo(node) {
        return node.nodeType === 1 && (node.tagName === "VIDEO" || node.tagName === "BWP-VIDEO" ||
            !!(node.querySelector && node.querySelector(mediaSelector)));
    }

    window.__eaghBridge = Object.freeze({ version: 1, execute: execute, setFocus: setFocus, state: function () { return snapshot(selectVideo()); } });
}());
