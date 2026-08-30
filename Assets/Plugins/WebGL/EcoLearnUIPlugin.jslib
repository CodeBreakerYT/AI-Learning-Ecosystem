// Lets StartAuthBridge.cs (the "Start Auth Bridge" GameObject in StartScene) call
// into the HTML/CSS login overlay defined by the EcoLearn WebGL template
// (Assets/WebGLTemplates/EcoLearn/index.html), which exposes window.EcoLearnUI.
mergeInto(LibraryManager.library, {

  EcoLearnHideOverlay: function () {
    if (window.EcoLearnUI && window.EcoLearnUI.hideOverlay) {
      window.EcoLearnUI.hideOverlay();
    }
  },

  EcoLearnSetStatus: function (isRegisterInt, messagePtr) {
    var message = UTF8ToString(messagePtr);
    if (window.EcoLearnUI && window.EcoLearnUI.setStatus) {
      window.EcoLearnUI.setStatus(!!isRegisterInt, message);
    }
  }

});
