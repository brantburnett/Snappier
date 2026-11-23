// Removes the "Edit this page" links from API reference pages while keeping "View Source".
// On API pages these links open GitHub's new-file page for an apiSpec override file, which this project does not use.

exports.preTransform = function (model) {
  return model;
}

exports.postTransform = function (model) {
  removeDocUrl(model);
  return model;
}

function removeDocUrl(item) {
  if (!item) {
    return;
  }

  delete item.docurl;

  if (Array.isArray(item.children)) {
    item.children.forEach(removeDocUrl);
  }
}
