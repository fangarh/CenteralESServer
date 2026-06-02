(function () {
  function createConfirmAction(options) {
    const showAlert = options.showAlert;

    return function confirmAction(title, message, requireComment) {
      const dialog = document.getElementById("confirm-dialog");
      const titleEl = document.getElementById("confirm-title");
      const messageEl = document.getElementById("confirm-message");
      const commentEl = document.getElementById("confirm-comment");
      const labelEl = document.getElementById("confirm-comment-label");
      const okButton = document.getElementById("confirm-ok");

      titleEl.textContent = title;
      messageEl.textContent = message;
      commentEl.value = "";
      commentEl.required = requireComment;
      labelEl.firstChild.textContent = requireComment ? "Комментарий обязателен" : "Комментарий";
      okButton.textContent = title;

      return new Promise(resolve => {
        const closeHandler = () => {
          dialog.removeEventListener("close", closeHandler);
          if (dialog.returnValue !== "ok") {
            resolve(null);
            return;
          }
          const comment = commentEl.value.trim();
          if (requireComment && !comment) {
            showAlert("Для этого действия нужен комментарий.", true);
            resolve(null);
            return;
          }
          resolve(comment);
        };
        dialog.addEventListener("close", closeHandler);
        dialog.showModal();
      });
    };
  }

  window.CenteralESAdminDialog = {
    createConfirmAction
  };
})();
